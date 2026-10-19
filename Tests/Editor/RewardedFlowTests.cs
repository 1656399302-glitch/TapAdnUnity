using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WXGameMaker.TapAdn.Tests
{
    public sealed class RewardedFlowTests
    {
        private TapAdnConfig config;
        private FakeBackend backend;
        private TapAdnService service;
        [SetUp] public void Setup()
        {
            config = ScriptableObject.CreateInstance<TapAdnConfig>();
            config.MediaId = 1; config.MediaKey = "test-only-placeholder"; config.MediaName = "Unit test";
            config.Placements = new[] { new RewardedPlacement { Key = "rewarded", SpaceId = 1 } };
            backend = new FakeBackend(); service = new TapAdnService(backend);
        }
        [TearDown] public void Cleanup() { service.Dispose(); UnityEngine.Object.DestroyImmediate(config); }
        private void Initialize() => service.InitializeAsync(config, new TapAdnPrivacyOptions { HasConsent = true }).GetAwaiter().GetResult();
        private void Load() => service.LoadRewardedAsync("rewarded").GetAwaiter().GetResult();
        private static string Code(Task task) => ((TapAdnException)task.Exception.GetBaseException()).Code;

        [Test] public void RefusedConsentNeverTouchesBackend()
        {
            var task = service.InitializeAsync(config, new TapAdnPrivacyOptions());
            Assert.That(Code(task), Is.EqualTo("consent_required")); Assert.That(backend.Initializations, Is.Zero);
        }
        [Test] public void OptionalPrivacySettingsDefaultToOff()
        {
            Initialize(); Assert.That(backend.Privacy.PersonalizedAds, Is.False); Assert.That(backend.Privacy.AllowOaid, Is.False);
            Assert.That(backend.Privacy.AllowAndroidId, Is.False); Assert.That(backend.Privacy.AllowSensors, Is.False);
        }
        [Test] public void ConfigurationIsValidatedBeforeInitialization()
        { config.MediaId = 0; var task = service.InitializeAsync(config, new TapAdnPrivacyOptions { HasConsent = true }); Assert.That(Code(task), Is.EqualTo("invalid_config")); Assert.That(backend.Initializations, Is.Zero); }
        [Test] public void DuplicatePlacementKeysAreRejected()
        { config.Placements = new[] { new RewardedPlacement { Key = "same", SpaceId = 1 }, new RewardedPlacement { Key = "same", SpaceId = 2 } }; Assert.Throws<TapAdnException>(config.Validate); }
        [Test] public void InitializationIsIdempotent()
        { Initialize(); Initialize(); Assert.That(backend.Initializations, Is.EqualTo(1)); }
        [Test] public void NoRewardFromShownOrClosed()
        {
            Initialize(); Load(); int rewards = 0, closed = 0;
            service.ShowRewarded("rewarded", new RewardedCallbacks { RewardVerified = _ => rewards++, Closed = _ => closed++ });
            backend.Ad.EmitShown(); backend.Ad.EmitReward(false); backend.Ad.EmitClose(); backend.Ad.EmitClose();
            Assert.That(rewards, Is.Zero); Assert.That(closed, Is.EqualTo(1)); Assert.That(service.IsShowing, Is.False);
        }
        [Test] public void VerifiedRewardIsDeliveredExactlyOnce()
        {
            Initialize(); Load(); int rewards = 0; string id = null;
            service.ShowRewarded("rewarded", new RewardedCallbacks { RewardVerified = session => { rewards++; id = session; } });
            backend.Ad.EmitReward(true); backend.Ad.EmitReward(true); backend.Ad.EmitClose();
            Assert.That(rewards, Is.EqualTo(1)); Assert.That(id.Length, Is.EqualTo(32));
        }
        [Test] public void CloseBeforeVerificationStillDeliversOneReward()
        {
            Initialize(); Load(); int rewards = 0;
            service.ShowRewarded("rewarded", new RewardedCallbacks { RewardVerified = _ => rewards++ });
            backend.Ad.EmitClose(); backend.Ad.EmitReward(true); backend.Ad.EmitReward(true);
            Assert.That(rewards, Is.EqualTo(1)); Assert.That(service.IsShowing, Is.False);
        }
        [Test] public void SecondClickCannotShowAnotherAdWhileActive()
        {
            Initialize(); Load(); int errors = 0;
            service.ShowRewarded("rewarded", new RewardedCallbacks());
            service.ShowRewarded("rewarded", new RewardedCallbacks { Error = e => { Assert.That(e.Code, Is.EqualTo("busy")); errors++; } });
            Assert.That(backend.Ad.Shows, Is.EqualTo(1)); Assert.That(errors, Is.EqualTo(1));
        }
        [Test] public void ShowFailureClearsSessionAndNeverRewards()
        {
            Initialize(); Load(); backend.Ad.ShowResult = false; int errors = 0, rewards = 0;
            service.ShowRewarded("rewarded", new RewardedCallbacks { Error = _ => errors++, RewardVerified = _ => rewards++ });
            backend.Ad.EmitReward(true); Assert.That(errors, Is.EqualTo(1)); Assert.That(rewards, Is.Zero); Assert.That(service.IsShowing, Is.False);
            backend.Ad = new FakeAd(); Load(); Assert.That(service.IsRewardedReady("rewarded"), Is.True);
        }
        [Test] public void NoFillAllowsRetry()
        {
            Initialize(); backend.NoFill = true;
            Assert.That(Code(service.LoadRewardedAsync("rewarded")), Is.EqualTo("no_fill"));
            backend.NoFill = false; Load(); Assert.That(service.IsRewardedReady("rewarded"), Is.True);
        }
        [Test] public void BackgroundPreventsNewRequests()
        {
            Initialize(); service.SetForeground(false);
            Assert.That(Code(service.LoadRewardedAsync("rewarded")), Is.EqualTo("background"));
            Assert.That(backend.Loads, Is.Zero); service.SetForeground(true); Load();
            service.SetForeground(false); int errors = 0;
            service.ShowRewarded("rewarded", new RewardedCallbacks { Error = _ => errors++ }); Assert.That(errors, Is.EqualTo(1)); Assert.That(backend.Ad.Shows, Is.Zero);
        }
        [Test] public void ExpiredAdCannotBeShown()
        { Initialize(); Load(); backend.Ad.Ready = false; Assert.That(service.IsRewardedReady("rewarded"), Is.False); }
        [Test] public void DisposingReleasesAdsAndSuppressesLateRewards()
        {
            Initialize(); Load(); int rewards = 0;
            service.ShowRewarded("rewarded", new RewardedCallbacks { RewardVerified = _ => rewards++ }); service.Dispose();
            backend.Ad.EmitReward(true); Assert.That(rewards, Is.Zero); Assert.That(backend.Ad.Disposals, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator LoadTimeoutDisposesLateAdWithoutShowingIt()
        {
            config.LoadTimeoutSeconds = 0.02f; Initialize(); backend.PendingLoad = new TaskCompletionSource<IRewardedAd>();
            var task = service.LoadRewardedAsync("rewarded");
            while (!task.IsCompleted) yield return null;
            Assert.That(Code(task), Is.EqualTo("load_timeout"));
            backend.PendingLoad.SetResult(backend.Ad);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(backend.Ad.Disposals, Is.EqualTo(1)); Assert.That(backend.Ad.Shows, Is.Zero);
            backend.PendingLoad = null; backend.Ad = new FakeAd(); Load(); Assert.That(service.IsRewardedReady("rewarded"), Is.True);
        }
        [UnityTest] public IEnumerator InitializationTimeoutDoesNotEnableAdvertising()
        {
            config.InitializationTimeoutSeconds = 0.02f; backend.PendingInit = new TaskCompletionSource<bool>();
            var task = service.InitializeAsync(config, new TapAdnPrivacyOptions { HasConsent = true });
            while (!task.IsCompleted) yield return null;
            Assert.That(Code(task), Is.EqualTo("initialization_timeout")); backend.PendingInit.SetResult(true);
            yield return null; Assert.That(service.IsInitialized, Is.False);
        }
        [UnityTest] public IEnumerator DuplicateLoadIsRejectedAndDisposeCancelsPendingLoad()
        {
            Initialize(); backend.PendingLoad = new TaskCompletionSource<IRewardedAd>();
            var first = service.LoadRewardedAsync("rewarded");
            Assert.That(Code(service.LoadRewardedAsync("rewarded")), Is.EqualTo("busy")); service.Dispose();
            while (!first.IsCompleted) yield return null;
            Assert.That(Code(first), Is.EqualTo("disposed")); backend.PendingLoad.SetResult(backend.Ad);
            yield return null;
        }

        private sealed class FakeBackend : ITapAdnBackend
        {
            public int Initializations, Loads;
            public bool NoFill;
            public FakeAd Ad = new FakeAd();
            public TapAdnPrivacyOptions Privacy;
            public TaskCompletionSource<IRewardedAd> PendingLoad;
            public TaskCompletionSource<bool> PendingInit;
            public Task InitializeAsync(TapAdnConfig config, TapAdnPrivacyOptions privacy, CancellationToken cancellation)
            { Initializations++; Privacy = privacy; return PendingInit != null ? PendingInit.Task : Task.CompletedTask; }
            public Task<IRewardedAd> LoadRewardedAsync(long id, CancellationToken cancellation)
            { Loads++; if (NoFill) throw new TapAdnException("no_fill", "Test no fill"); return PendingLoad != null ? PendingLoad.Task : Task.FromResult<IRewardedAd>(Ad); }
            public void Dispose() { }
        }
        private sealed class FakeAd : IRewardedAd
        {
            public bool Ready = true, ShowResult = true;
            public int Shows, Disposals;
            public bool IsReady => Ready;
            public event Action Shown;
            public event Action<bool> RewardVerified;
            public event Action Closed;
            public event Action<TapAdnException> Failed;
            public bool Show() { Shows++; Ready = false; return ShowResult; }
            public void EmitShown() => Shown?.Invoke();
            public void EmitReward(bool verified) => RewardVerified?.Invoke(verified);
            public void EmitClose() => Closed?.Invoke();
            public void Dispose() { Disposals++; Ready = false; }
        }
    }
}
