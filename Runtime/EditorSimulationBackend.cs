#if UNITY_EDITOR
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZZWGameMaker.TapAdn
{
    public enum SimulatedAdOutcome { Rewarded, ClosedWithoutReward, NoFill, ShowFailure }

    /// <summary>Explicit opt-in only; excluded from players. Never represents a real ad request.</summary>
    public sealed class EditorSimulationBackend : ITapAdnBackend
    {
        public SimulatedAdOutcome Outcome { get; set; } = SimulatedAdOutcome.Rewarded;
        public Task InitializeAsync(TapAdnConfig config, TapAdnPrivacyOptions privacy, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task<IRewardedAd> LoadRewardedAsync(long spaceId, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (Outcome == SimulatedAdOutcome.NoFill) throw new TapAdnException("simulated_no_fill", "Editor simulation: no fill.");
            return Task.FromResult<IRewardedAd>(new SimulatedAd(Outcome));
        }
        public void Dispose() { }
        private sealed class SimulatedAd : IRewardedAd
        {
            private readonly SimulatedAdOutcome outcome;
            public bool IsReady { get; private set; } = true;
            public event Action Shown;
            public event Action<bool> RewardVerified;
            public event Action Closed;
            public event Action<TapAdnException> Failed;
            public SimulatedAd(SimulatedAdOutcome outcome) { this.outcome = outcome; }
            public bool Show()
            {
                if (!IsReady) return false;
                IsReady = false;
                if (outcome == SimulatedAdOutcome.ShowFailure)
                { Failed?.Invoke(new TapAdnException("simulated_show_failure", "Editor simulation: show failed.")); return false; }
                Shown?.Invoke();
                if (outcome == SimulatedAdOutcome.Rewarded) RewardVerified?.Invoke(true);
                Closed?.Invoke();
                return true;
            }
            public void Dispose() { IsReady = false; }
        }
    }
}
#endif
