using System;
using System.Threading;
using System.Threading.Tasks;

namespace WXGameMaker.TapAdn
{
    // Backend extension point; gameplay should depend on TapAdnService instead.
    public interface ITapAdnBackend : IDisposable
    {
        Task InitializeAsync(TapAdnConfig config, TapAdnPrivacyOptions privacy, CancellationToken cancellation);
        Task<IRewardedAd> LoadRewardedAsync(long spaceId, CancellationToken cancellation);
    }

    public interface IRewardedAd : IDisposable
    {
        bool IsReady { get; }
        event Action Shown;
        event Action<bool> RewardVerified;
        event Action Closed;
        event Action<TapAdnException> Failed;
        bool Show();
    }

    public static class TapAdnBackendRegistry
    {
        public static Func<ITapAdnBackend> AndroidFactory { private get; set; }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { AndroidFactory = null; }

        public static ITapAdnBackend CreateAndroid()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (AndroidFactory != null) return AndroidFactory();
            throw new TapAdnException("sdk_missing", "Open Tools/TapADN/SDK Setup and repair the SDK installation.");
#else
            throw new TapAdnException("unsupported_platform", "Real ads require Android. Select Editor simulation explicitly.");
#endif
        }
    }
}
