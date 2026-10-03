# Local verification — 3 October 2026

## Passed

- ASP.NET Core API Release compilation in Docker using .NET 10; PostgreSQL migration, demonstration seed, durable records and database health.
- Final Angular production bundle using Node **24.14.1**: **220.47 kB** raw initial bundle. Node 24 is pinned at both repository/web scope; npm installation enforces the major version; CI uses Node 24.
- **13 C# tests**, all passing: password hashing, required checklists, technician lifecycle restrictions, closed work protection, duplicate quantities, SQLite restart/outbox/photo recovery, immutable lost-acknowledgment replay, explicit conflict resolution, atomic photo references, rejection correction with archived evidence, incoming photograph cache, and restored assignment access without draft loss.
- Real HTTP/API/PostgreSQL workflow: authentication, role and assignment authorization, site/asset create/update/delete and history protection, safe attachment upload/replay and invalid image rejection, null input validation, concurrent duplicate submission, exact receipt replay, changed-payload rejection, exactly-once stock, stale versions, supervisor review/history/faults/reports, schedule creation/edit/pause/enable/repeated generation, stale inventory/schedule protection, technician profile edit, retired-asset generation exclusion and reassignment retaining photograph evidence.
- **Actual mobile SQLite engine against the live API/PostgreSQL**: scheduled assignment; offline start/checklist/notes/fault/photo/parts/submission; process restart; exact photo retention; disconnected request; deliberately lost response after a real server commit; identical frozen request replay after another restart; supervisor review before response recovery; local reconciliation to Completed; stock/fault/consumption exactly once; persisted photo bytes/history/reports/audit; corrective fault linkage; a fresh photo cache; stock rejection rollback and explicit draft correction.
- Angular browser verification: real supervisor login and metrics; completed inspection/evidence/audit viewing; schedule edit and stale-version recovery; technician profile update; acknowledged inventory adjustment using the inline form; persisted reports. Verification data is retained in `CHECK-*`/`JOURNEY-*` records.
- Final native Android MAUI Debug APK compiled and exported with **zero compiler warnings and zero errors**. Photo selection uses `PickPhotosAsync` with a single-item limit. Artifact: `artifacts/android/com.fieldservice.maintenance-Signed.apk`, SHA-256 `7bb7e57796791a4bc39d91fb6d5a5ebc89b5a02eeb8f1c40ec56f72374c6f381`.
- Shell/Python verification script syntax and `git diff --check`.

## Native device acceptance

Emulator acceptance was deferred at the user's direction for this backend/frontend integration and UI-redesign checkpoint. The virtual device booted, but the complete native GUI journey was not executed. Camera/picker permissions, secure storage and native page lifecycle remain **unverified on a device**. The live SQLite/API acceptance test verifies persistence and synchronization independently of native widgets. A physical Android device can be used for later acceptance; an emulator is optional.

Temporary emulator tooling was removed to recover disk space; the final APK is retained. The final builds passed after sequencing resource-intensive checks. No unrelated project data or Docker volumes were deleted. API/database records are persisted and Compose services have restart policies.

## Optional/external verification

- iOS and Mac Catalyst are optional targets and have not been compiled or run; Xcode is unavailable here.
- GitHub Actions is configured for Angular, C# tests, PostgreSQL checks, the live mobile-core journey and Android compilation. It has not been executed on GitHub in this work. Local equivalent checks are recorded above.
- Trusted production HTTPS, production credentials and deployment are environment configuration outside this local UI-redesign checkpoint.
