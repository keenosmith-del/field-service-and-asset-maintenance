# Local verification — 3 October 2026

## Passed

- ASP.NET Core API Release compilation in Docker using the .NET 10 SDK.
- PostgreSQL migration, demo seed, container recreation, health check and persisted records.
- Angular production build (217.53 kB raw initial bundle).
- Angular browser checks: supervisor login, real metrics, site creation, completed inspection details, checklist answers, photograph loading, consumed parts and audit records.
- Ten C# tests: password hashing; mandatory checklist validation; technician lifecycle restrictions; closed work protection; duplicate part validation; SQLite/outbox/photo recovery after restart; immutable operation replay after a lost acknowledgment; conflict retention and explicit resolution; atomic photo/outbox reference persistence.
- Real API/PostgreSQL workflow: authentication, unauthenticated rejection, supervisor/technician RBAC, assignment ownership, attachment retries and invalid image rejection, null payload validation, concurrent duplicate submission, identical persisted receipt replay, changed-payload collision rejection, exactly-once stock consumption, stale-version conflicts, supervisor review, completed asset history, fault history, reports, repeated schedule generation, and reassignment retaining earlier photo evidence.

The workflow script creates `CHECK-*` records in the demonstration database and retains them for audit. The original seeded pump inspection remains available for a hands-on demonstration. Credentials and signing keys are in ignored `.env`, not in source control.

## Native verification

The Linux ARM64 Android toolchain could not load native build dependencies (`Mono.Unix`, then `libZipSharpNative-3-3`). Installing Google's SDK components and switching the complete build environment to Linux x86_64 resolved the architecture mismatch. The native MAUI application source compiled using the genuine Android workload. The complete Android Debug APK build succeeded with zero errors. One warning reports that single-photo selection uses MAUI's still-supported, obsolete `PickPhotoAsync`; multi-photo selection is not enabled. The APK is exported to `artifacts/android` (ignored by Git).

No Android emulator, Android SDK, host .NET SDK, or Xcode was present on this Mac. Android camera/picker permissions, secure storage, native page lifecycle, connectivity notifications, and the complete device offline/restart/sync journey have **not** been executed. iOS and Mac Catalyst targets have **not** been compiled or run.

GitHub Actions is configured for Angular, C# checks, PostgreSQL workflow checks and Android compilation. The workflow has not been run on GitHub in this execution.
