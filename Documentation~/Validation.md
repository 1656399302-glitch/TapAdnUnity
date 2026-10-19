# Validation record

Validated on 2026-10-19 with Unity 6000.0.59f2 on macOS.

- SelfGameFrameWork: required `Tool/verify/unity-compile-check.sh` passed with the installed vendor SDK and rewarded adapter.
- Existing project: 27/27 EditMode tests passed, no skips.
- Minimal project containing this package, Unity engine module dependencies and Unity Test Framework: initial import compiled before the vendor SDK existed; official download/installation succeeded; 27/27 EditMode tests passed, no skips. The optional rewarded example also compiled.
- Generated Java bridges compiled with the actual 5.3.0.4 AAR, Unity Android classes and Android API 36. Reproduce using `Tests~/verify_android_bridge.py`.
- Official download matched MD5 `1e2e00686634f1944e82cce48513d9bc` and SHA-256 `32ef523d7d45af42572f6f0d4c8a87dbc027f371e9db38f7a16922e4b0eb51ab`.
- Tests cover consent, privacy defaults, config validation, verified reward deduplication and close/reward order, concurrent requests, no fill, retry, background gating, disposal, initialization/load timeouts, late ad cleanup, checksums, archive paths, vendor SemVer, failed-install preservation and generated Gradle/Manifest idempotency.

No APK/AAB was built or exported. No real media credentials were used, and no device ad request, reward, revenue, Gradle dependency resolution, IL2CPP player link or merged release Manifest was verified. These require an authorized Android build, configured direct media/placement, and a device.
