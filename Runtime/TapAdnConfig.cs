using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZZWGameMaker.TapAdn
{
    [Serializable]
    public sealed class RewardedPlacement
    {
        public string Key;
        public long SpaceId;
    }

    [CreateAssetMenu(menuName = "TapADN/Local configuration", fileName = "TapAdnLocalConfig")]
    public sealed class TapAdnConfig : ScriptableObject
    {
        public long MediaId;
        [SerializeField, HideInInspector] private string mediaKey;
        public string MediaName;
        public bool EnableDebug;
        [Min(0.01f)] public float InitializationTimeoutSeconds = 15;
        [Min(0.01f)] public float LoadTimeoutSeconds = 30;
        public RewardedPlacement[] Placements = Array.Empty<RewardedPlacement>();
        public string MediaKey { get => mediaKey; set => mediaKey = value; }

        public long GetSpaceId(string key)
        {
            foreach (var placement in Placements)
                if (placement != null && placement.Key == key) return placement.SpaceId;
            throw new TapAdnException("unknown_placement", "The rewarded placement is not configured.");
        }

        public void Validate()
        {
            if (MediaId <= 0 || string.IsNullOrWhiteSpace(MediaKey) || string.IsNullOrWhiteSpace(MediaName))
                throw new TapAdnException("invalid_config", "Media ID, key and name must be configured locally.");
            if (!ValidTimeout(InitializationTimeoutSeconds) || !ValidTimeout(LoadTimeoutSeconds))
                throw new TapAdnException("invalid_config", "Timeouts must be finite and between 0 and 3600 seconds.");
            if (Placements == null || Placements.Length == 0)
                throw new TapAdnException("invalid_config", "Configure at least one rewarded placement.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var placement in Placements)
                if (placement == null || string.IsNullOrWhiteSpace(placement.Key) || placement.SpaceId <= 0 || !keys.Add(placement.Key))
                    throw new TapAdnException("invalid_config", "Placement keys must be unique and space IDs must be positive.");
        }

        private static bool ValidTimeout(float value) => value > 0 && value <= 3600 && !float.IsNaN(value);
    }

    // Construct from a real consent event. All optional processing is opt-in.
    public sealed class TapAdnPrivacyOptions
    {
        public bool HasConsent { get; set; }
        public bool PersonalizedAds { get; set; }
        public bool AllowOaid { get; set; }
        public bool AllowAndroidId { get; set; }
        public bool AllowSensors { get; set; }
    }

    public sealed class TapAdnException : Exception
    {
        public string Code { get; }
        public TapAdnException(string code, string message) : base(message) { Code = code; }
    }

    public sealed class RewardedCallbacks
    {
        public Action<string> Shown;
        // The session ID is an idempotency key for the game's own reward/save operation.
        public Action<string> RewardVerified;
        public Action<string> Closed;
        public Action<TapAdnException> Error;
    }
}
