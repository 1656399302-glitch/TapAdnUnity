using System.Threading.Tasks;
using UnityEngine;
using ZZWGameMaker.TapAdn;

public sealed class RewardedVideoExample : MonoBehaviour
{
    [SerializeField] private TapAdnConfig localConfig;
    [SerializeField] private bool editorSimulation;
    [SerializeField, Min(1)] private int demoRewardAmount = 10;
    private TapAdnService service;

    // Connect ONLY to the real privacy UI's accept event. Installation/startup is not consent.
    public async void OnPrivacyPolicyAccepted()
    {
        if (service != null) return;
        try
        {
#if UNITY_EDITOR
            service = editorSimulation ? new TapAdnService(new EditorSimulationBackend()) : TapAdnService.CreateAndroid();
#else
            service = TapAdnService.CreateAndroid();
#endif
            var config = localConfig != null ? localConfig : Resources.Load<TapAdnConfig>("TapAdnLocalConfig");
            await service.InitializeAsync(config, new TapAdnPrivacyOptions { HasConsent = true });
            await service.LoadRewardedAsync("rewarded");
        }
        catch (TapAdnException error) { Debug.LogWarning("Advertising unavailable: " + error.Code); service?.Dispose(); service = null; }
    }

    public void OnWatchRewardedPressed()
    {
        if (service == null || !service.IsRewardedReady("rewarded")) return;
        service.ShowRewarded("rewarded", new RewardedCallbacks
        {
            RewardVerified = session => GrantLocalReward(session),
            Closed = session => { /* Restore gameplay here. Preload again on a later foreground game event. */ },
            Error = error => Debug.LogWarning("Advertising unavailable: " + error.Code)
        });
    }
    public async Task PreloadAgainAsync() { if (service != null) await service.LoadRewardedAsync("rewarded"); }
    public void OnPrivacyConsentWithdrawn() { service?.Dispose(); service = null; }
    private void GrantLocalReward(string sessionId)
    {
        // Demonstration save only. Replace this isolated record with the game's inventory transaction.
        const string key = "TapAdnExample.LocalReward";
        var save = JsonUtility.FromJson<DemoSave>(PlayerPrefs.GetString(key, "{}")) ?? new DemoSave();
        if (save.LastSession == sessionId) return;
        save.Coins += demoRewardAmount;
        save.LastSession = sessionId;
        PlayerPrefs.SetString(key, JsonUtility.ToJson(save));
        PlayerPrefs.Save();
        Debug.Log("Demo reward saved. Demo coin balance: " + save.Coins);
    }
    [System.Serializable] private sealed class DemoSave { public int Coins; public string LastSession; }
    private void OnDestroy() { service?.Dispose(); service = null; }
}
