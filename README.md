# TapADN Rewarded Ads for Unity

Unity **6000.0.59f2** with Android Build Support (SDK/NDK/OpenJDK), Android, TapADN **direct** rewarded video. No TapSDK login, IAP, mediation, or game backend is installed.

## Install

In Package Manager choose **Add package from git URL**:

```text
https://github.com/1656399302-glitch/TapAdnUnity.git#v0.1.0
```

Private repositories require Git authentication outside Unity (credential helper or an authenticated SSH URL). The package has no nested Git dependencies or scoped registry requirement.

After initial script compilation, the installer downloads official SDK **5.3.0.4** from TapADN, validates MD5 and SHA-256, and generates `Packages/com.zzwgamemaker.tapadn.vendor`. A second script compilation makes the backend available. No advertising SDK initialization happens during installation. Keep the Editor open until **Tools > TapADN > SDK Setup** reports verified and ready.

First-time setup needs internet. Android builds resolve `com.squareup.okhttp3:okhttp:3.12.1` and its transitive dependencies from Maven Central (or Gradle's cache). This is not an offline distribution.

The installer uses a temporary staging directory, restores the previous complete install if replacement fails, and retains a verified archive in `Library/TapAdn`. Repair from cache or download again in SDK Setup. Generated files are checksummed; do not edit them. An installation failure leaves the public API compilable; real Android builds fail clearly until setup succeeds. Automatic download is attempted once per Editor session to avoid retry loops.

For CI, import/compile the package first, then run a separate Unity invocation:

```sh
"$UNITY_EXECUTABLE" -batchmode -nographics -projectPath /path/to/game \
  -executeMethod ZZWGameMaker.TapAdn.Editor.SdkInstaller.InstallForBatchMode -quit
```

Run the project's script compilation check after this invocation. SDK installation is separate from a player build.

## Configure per game

Choose **Tools > TapADN > Create Local Configuration**. Fill in the **direct-mode** media ID, Media Key, media name and rewarded Space ID; the default gameplay key is `rewarded`.

The local asset is `Assets/TapAdnLocal/Resources/TapAdnLocalConfig.asset`. Media values must come from your own Dirichlet account. Placeholder values cannot request real ads. The installer adds generated SDK and local configuration exclusions to `.git/info/exclude` when available; for teams and worktrees, also add these rules to the game repository's `.gitignore`:

```gitignore
**/Packages/com.zzwgamemaker.tapadn.vendor/
**/Packages/com.zzwgamemaker.tapadn.vendor.meta
**/Assets/TapAdnLocal/
**/Assets/TapAdnLocal.meta
```

The Media Key is a client-required parameter and ships in the built game. Keep it out of public source control and logs. Never use server signing/security keys as client configuration.

## Use after privacy consent

Own one `TapAdnService` for the game and call its methods on Unity's main thread. Call initialization only from the host's real consent flow:

```csharp
var config = Resources.Load<TapAdnConfig>("TapAdnLocalConfig");
var ads = TapAdnService.CreateAndroid();
await ads.InitializeAsync(config, new TapAdnPrivacyOptions { HasConsent = true });
await ads.LoadRewardedAsync("rewarded");

if (ads.IsRewardedReady("rewarded"))
    ads.ShowRewarded("rewarded", new RewardedCallbacks
    {
        RewardVerified = sessionId => GrantAndSaveRewardOnce(sessionId),
        Closed = sessionId => RestoreGameplay(),
        Error = error => ShowAdvertisingUnavailable(error.Code)
    });
```

`GrantAndSaveRewardOnce`, `RestoreGameplay` and `ShowAdvertisingUnavailable` are game-owned functions. The Package never chooses a reward value or edits a game save. Use the session ID as an idempotency key for your local save operation. Rewarded placement IDs must match the platform's rewarded-video type.

Only a verified reward event grants eligibility; shown, clicked, closed, skipped and timed-out events never do. A two-second routing grace period handles close-before-reward ordering; callbacks arriving after release/disposal are ignored. Closed is a UI lifecycle event, not a reward result. Do not dispose the service on normal ad close if you want to receive an out-of-order verification event.

Initialization/load timeouts default to 15/30 seconds. Failed loads can be retried explicitly; no automatic ad display or background retry occurs. No fill, offline, expired ads, denied consent and other errors must leave normal offline gameplay usable. Do not preload synchronously from `Closed`: ad Activities can still be returning to the game; wait for a later foreground game event.

The lifecycle component blocks new operations while the game is paused/unfocused and does not interrupt an ad Activity's normal transition. Call `Dispose` when the owner is destroyed or consent is withdrawn. Disposal stops new requests and suppresses future callbacks; the underlying third-party SDK does not expose a full process-wide deinitialization API. Changing optional privacy choices after SDK initialization requires disposing the service, then explicitly initializing a new service with the updated consent/options. Global controller switches are updated before any new request; already displayed native creatives cannot be recalled through this API.

Personalized ads, OAID, Android ID, sensors and shake interaction are disabled by default. Phone state, location, Wi-Fi identifiers, external storage and installed-app-list collection are always disabled in this integration. No sensitive runtime permission prompt is issued. The generated Manifest removes the SDK's optional install/query/OAID-vendor permission declarations with an SDK-scoped merger selector; permissions explicitly owned by the host remain the host's responsibility. Include actual SDK processing in your game's privacy policy.

In the Editor, opt in explicitly:

```csharp
var ads = new TapAdnService(new EditorSimulationBackend
    { Outcome = SimulatedAdOutcome.Rewarded });
```

Simulation code is excluded from players. `CreateAndroid()` fails on unsupported platforms; it never silently pretends a real ad succeeded. Import **Rewarded video flow** from Package Manager Samples for a small consent/event adapter without a prefab.

## SDK and build layout

- Original repository: facade, installer, build hook, privacy bridge, backend template and tests.
- Locally generated vendor package: official C# Runtime/Resources, Android AAR, Java source under `Native~`, and the compiled rewarded adapter.
- The official Java bridge's empty custom controller is replaced with our explicit privacy controller; its initialization result checks `TapAdSdk.isInitialized()`.
- Only generated Gradle files are changed: Java sources, Maven dependency, SDK-scoped optional permission removals and native JNI keep rules. Host templates, launcher Activity and project settings are preserved.
- Official sample/iOS/host-Manifest/build-postprocessor files are not imported. Third-party files remain subject to their own terms.

## Validate and release

Add `com.zzwgamemaker.tapadn` to the host manifest's `testables` array to run its EditMode tests. Coverage includes consent, opt-in defaults, reward deduplication/order, timeouts, late handles, retry, concurrent calls, background operation, archive safety and idempotent build transforms.

Script compilation and mock tests do not establish Android device compatibility, ad fill or revenue. A separately authorized Android player build must validate the final Merged Manifest, Java/native libraries, IL2CPP preservation, release logging, actual SDK initialize/load/show/reward and foreground recovery. Keep the game's min API/ARM64 settings unless there is a concrete compatibility issue.

Release changes under new immutable Git tags; never change the SDK URL or hashes independently of a reviewed package release. Update `SdkLock.AdapterRevision` when generated adaptations change. Remove this package through Package Manager and delete only its generated vendor package/local configuration when uninstalling; the local download cache may also be deleted. There is no runtime dependency on this package once those files and gameplay calls are removed.

Official references: [Unity SDK](https://ssp.dirichlet.cn/docs/dirichlet-sdk/sdk-guide-unity/), [downloads](https://ssp.dirichlet.cn/docs/resource-download/), [Android SDK](https://ssp.dirichlet.cn/docs/dirichlet-sdk/sdk-guide/), [privacy](https://ssp.dirichlet.cn/docs/compliance/), [personalization](https://ssp.dirichlet.cn/docs/personalized/), [media setup](https://ssp.dirichlet.cn/docs/ssp-guide/).

Validation details: [Validation record](Documentation~/Validation.md).
