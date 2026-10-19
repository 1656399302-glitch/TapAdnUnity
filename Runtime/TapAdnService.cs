using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ZZWGameMaker.TapAdn
{
    /// <summary>Own on Unity's main thread; Dispose when the game no longer permits ads.</summary>
    public sealed class TapAdnService : IDisposable
    {
        private readonly ITapAdnBackend backend;
        private readonly SynchronizationContext context;
        private readonly int threadId;
        private readonly Dictionary<string, IRewardedAd> ready = new Dictionary<string, IRewardedAd>();
        private readonly HashSet<string> loading = new HashSet<string>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly List<Session> retired = new List<Session>();
        private TapAdnConfig config;
        private Session active;
        private bool initializing, disposed, foreground = true;
        private int generation;
        private TapAdnLifecycle lifecycle;
        public bool IsInitialized { get; private set; }
        public bool IsShowing => active != null;

        public static TapAdnService CreateAndroid() => new TapAdnService(TapAdnBackendRegistry.CreateAndroid());

        public TapAdnService(ITapAdnBackend backend)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            threadId = Thread.CurrentThread.ManagedThreadId;
            context = SynchronizationContext.Current;
            if (Application.isPlaying)
            {
                var host = new GameObject("TapADN Lifecycle") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(host);
                lifecycle = host.AddComponent<TapAdnLifecycle>();
                lifecycle.Service = this;
                foreground = Application.isFocused;
            }
        }

        public async Task InitializeAsync(TapAdnConfig source, TapAdnPrivacyOptions privacy)
        {
            RequireMainThread();
            if (disposed) throw new TapAdnException("disposed", "Advertising service has been disposed.");
            if (privacy == null || !privacy.HasConsent)
                throw new TapAdnException("consent_required", "Explicit privacy consent is required before initialization.");
            RequireForeground();
            if (initializing) throw new TapAdnException("busy", "Initialization is already in progress.");
            if (IsInitialized) return;
            if (source == null) throw new TapAdnException("invalid_config", "A local configuration is required.");
            source.Validate();
            // Snapshot mutable assets/options before any asynchronous work.
            config = UnityEngine.Object.Instantiate(source);
            var consent = new TapAdnPrivacyOptions { HasConsent = true, PersonalizedAds = privacy.PersonalizedAds,
                AllowOaid = privacy.AllowOaid, AllowAndroidId = privacy.AllowAndroidId, AllowSensors = privacy.AllowSensors };
            initializing = true;
            var epoch = generation;
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(config.InitializationTimeoutSeconds));
                    await AwaitCancellation(backend.InitializeAsync(config, consent, timeout.Token), timeout.Token);
                    if (epoch != generation || disposed) throw new OperationCanceledException();
                    IsInitialized = true;
                }
            }
            catch (OperationCanceledException)
            {
                throw new TapAdnException(disposed ? "disposed" : "initialization_timeout", "Advertising initialization was cancelled or timed out.");
            }
            finally
            {
                initializing = false;
                if (!IsInitialized) ReleaseConfig();
            }
        }

        public async Task LoadRewardedAsync(string placementKey)
        {
            RequireAvailable();
            var spaceId = config.GetSpaceId(placementKey);
            if (!loading.Add(placementKey)) throw new TapAdnException("busy", "This placement is already loading.");
            if (ready.TryGetValue(placementKey, out var old)) { old.Dispose(); ready.Remove(placementKey); }
            var epoch = generation;
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(config.LoadTimeoutSeconds));
                    var task = backend.LoadRewardedAsync(spaceId, timeout.Token);
                    IRewardedAd ad;
                    try { ad = await AwaitCancellation(task, timeout.Token); }
                    catch
                    {
                        // A backend that cannot cancel its native request must still release a late handle.
                        _ = DisposeLateResult(task);
                        throw;
                    }
                    if (epoch != generation || disposed || !foreground || !ad.IsReady)
                    {
                        ad.Dispose();
                        throw new TapAdnException("load_cancelled", "The loaded ad is no longer usable.");
                    }
                    ready[placementKey] = ad;
                }
            }
            catch (OperationCanceledException)
            {
                throw new TapAdnException(disposed ? "disposed" : "load_timeout", "Advertising load was cancelled or timed out.");
            }
            finally { loading.Remove(placementKey); }
        }

        public bool IsRewardedReady(string placementKey)
        {
            RequireMainThread();
            return !string.IsNullOrEmpty(placementKey) && !disposed && IsInitialized && foreground && ready.TryGetValue(placementKey, out var ad) && ad.IsReady;
        }

        public void ShowRewarded(string placementKey, RewardedCallbacks callbacks)
        {
            callbacks = callbacks ?? new RewardedCallbacks();
            try
            {
                RequireAvailable();
                if (active != null) throw new TapAdnException("busy", "Another rewarded ad is being displayed.");
                if (!IsRewardedReady(placementKey)) throw new TapAdnException("not_ready", "Load a rewarded ad before showing it.");
                var ad = ready[placementKey];
                ready.Remove(placementKey);
                var session = new Session(Guid.NewGuid().ToString("N"), ad, callbacks);
                active = session;
                ad.Shown += () => Dispatch(() =>
                {
                    if (!disposed && !session.Ended && !session.Shown)
                    { session.Shown = true; Safe(() => callbacks.Shown?.Invoke(session.Id)); }
                });
                ad.RewardVerified += verified => Dispatch(() =>
                {
                    // A close callback can precede verification. Keep its session alive for a short grace period.
                    if (!disposed && !session.Released && verified && !session.Rewarded && !session.Failed)
                    { session.Rewarded = true; Safe(() => callbacks.RewardVerified?.Invoke(session.Id)); }
                });
                ad.Closed += () => Dispatch(() => End(session, null));
                ad.Failed += error => Dispatch(() => End(session, error));
                try
                {
                    if (!ad.Show()) End(session, new TapAdnException("show_failed", "The native SDK declined to show the ad."));
                }
                catch { End(session, new TapAdnException("show_failed", "The native SDK failed to show the ad.")); }
            }
            catch (TapAdnException error) { Safe(() => callbacks.Error?.Invoke(error)); }
        }

        public void SetForeground(bool value)
        {
            RequireMainThread();
            foreground = value;
            // Do not interrupt an ad's own Activity transition. No new requests are allowed in the background.
        }

        private void End(Session session, TapAdnException error)
        {
            if (disposed || session.Ended) return;
            session.Ended = true;
            session.Failed = error != null;
            if (ReferenceEquals(active, session)) active = null;
            // Retain routing briefly so close-before-reward ordering cannot erase a verified reward.
            retired.Add(session);
            _ = ReleaseSessionLater(session);
            if (error == null) Safe(() => session.Callbacks.Closed?.Invoke(session.Id));
            else Safe(() => session.Callbacks.Error?.Invoke(error));
        }

        private async Task ReleaseSessionLater(Session session)
        {
            try { await Task.Delay(2000, lifetime.Token); }
            catch (OperationCanceledException) { return; }
            session.Release();
            retired.Remove(session);
        }

        public void Dispose()
        {
            RequireMainThread();
            if (disposed) return;
            disposed = true; generation++; IsInitialized = false;
            lifetime.Cancel();
            foreach (var ad in ready.Values) ad.Dispose();
            ready.Clear();
            active?.Release(); active = null;
            foreach (var session in retired) session.Release();
            retired.Clear();
            backend.Dispose();
            ReleaseConfig();
            if (lifecycle != null) { lifecycle.Service = null; UnityEngine.Object.Destroy(lifecycle.gameObject); }
        }

        private async Task DisposeLateResult(Task<IRewardedAd> task)
        {
            try { var ad = await task; Dispatch(ad.Dispose); } catch { /* Original request reports the failure. */ }
        }

        private static async Task AwaitCancellation(Task task, CancellationToken token)
        {
            var cancelled = new TaskCompletionSource<bool>();
            using (token.Register(() => cancelled.TrySetCanceled()))
            {
                if (await Task.WhenAny(task, cancelled.Task) != task)
                { _ = Observe(task); token.ThrowIfCancellationRequested(); }
                await task;
                token.ThrowIfCancellationRequested();
            }
        }
        private static async Task<T> AwaitCancellation<T>(Task<T> task, CancellationToken token)
        { await AwaitCancellation((Task)task, token); return await task; }
        private static async Task Observe(Task task) { try { await task; } catch { } }

        private void RequireMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("Use TapAdnService on its owning Unity main thread.");
        }
        private void RequireForeground()
        { if (!foreground) throw new TapAdnException("background", "Advertising requests require the foreground."); }
        private void RequireAvailable()
        {
            RequireMainThread();
            if (disposed) throw new TapAdnException("disposed", "Advertising service has been disposed.");
            if (!IsInitialized) throw new TapAdnException("not_initialized", "Initialize advertising after consent first.");
            RequireForeground();
        }
        private void ReleaseConfig()
        {
            if (config == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(config); else UnityEngine.Object.DestroyImmediate(config);
            config = null;
        }
        private void Dispatch(Action action)
        {
            if (Thread.CurrentThread.ManagedThreadId == threadId) action();
            else if (context != null) context.Post(_ => action(), null);
            else throw new InvalidOperationException("A Unity synchronization context is required for background callbacks.");
        }
        private static void Safe(Action callback) { try { callback(); } catch (Exception e) { Debug.LogException(e); } }

        private sealed class Session
        {
            public readonly string Id;
            public readonly IRewardedAd Ad;
            public readonly RewardedCallbacks Callbacks;
            public bool Shown, Rewarded, Ended, Failed, Released;
            public Session(string id, IRewardedAd ad, RewardedCallbacks callbacks) { Id = id; Ad = ad; Callbacks = callbacks; }
            public void Release() { if (Released) return; Released = true; Ad.Dispose(); }
        }
    }
}
