# Requirements sign-off — 3 October 2026

**Status: functional implementation and backend/frontend integration signed off for UI redesign.** No known functional implementation gaps remain within this checkpoint. Optional native device acceptance is deferred at the user's direction.

Scope: the supplied Field Service & Asset Maintenance Management brief, through functional integration and the UI redesign handoff. This is a local portfolio/demo checkpoint, not a production deployment certification.

| Requirement | Implementation | Verification |
| --- | --- | --- |
| Authentication and roles | JWT sign-in, supervisor/technician RBAC, demo users, mobile secure token storage | Real HTTP sign-in, unauthenticated/foreign-role/ownership rejection; password hashing test; native storage acceptance recorded separately |
| Site and asset management | Connected CRUD, identifiers, category, location, state, intervals, retained history | Real API create/update/delete and history protection; Angular browser checks |
| Preventive scheduling | Checklists, dates, priority, assignment, edit, pause/enable, unique generation, retired asset exclusion | Real API repeated generation, edits and stale-version rejection; browser edit check |
| Supervisor operations | Persisted overview, technicians/profile edits, dispatch, faults, inspection evidence, review and audit | Real API workflow and browser sign-in/details/management |
| Native technician application | Genuine Android MAUI XAML/MVVM; job download/start/checklist/notes/fault/photo/parts/submit; correction controls | Android compilation and device acceptance recorded in verification.md |
| Offline execution | Per-user SQLite drafts, reference data, photo bytes, outbox and archives; restart recovery; clear delivery states | Actual SQLite file tests and live SQLite/API/PostgreSQL journey |
| Reliable synchronization | Stable frozen operation IDs/payloads, exact server receipts, serializable mutation, upload retry/backoff, manual/connectivity sync, explicit reconciliation | Lost acknowledgment and restart replay, concurrent duplicate submission, exact-once stock/faults, rejection correction and photo cache checks |
| Maintenance lifecycle | Assigned/In Progress/Submitted/Completed with supervisor review, audit, linked corrective work | Live journey verifies review before receipt recovery, completed history and corrective linkage |
| Reporting | Database-derived overview, overdue/completion/assignments/faults/parts, CSV, audited stock adjustment | API reports/consumption/history checks and browser reports |
| Security and validation | Roles/ownership, null/length/enum/checklist/quantity validation, bounded image upload, locked photo decisions, versions, environment secrets | Real RBAC/malformed/image/replay/stale-version checks; committed migrations and ignored secrets |

## Changes completed during final audit

- Definitively rejected submissions now require explicit correction instead of endlessly retrying the same invalid request. Rebase retains the original draft in an archive and creates editable progress; the corrected inspection must be submitted again.
- Server photograph bytes now download into SQLite for offline viewing and reassignment continuity. Active previews reflect the current draft's references; exports retain photo evidence.
- Photo dependencies are processed per queued operation so later uploads cannot block recovery of an earlier persisted acknowledgment.
- Draft input is snapshotted before awaiting the sync lock; closing waits for active mutations; restored assignment access is persisted.
- Native command cleanup always releases its busy gate, and connectivity-driven command updates run on the main thread. Local start/submission states are explicit.
- Native checklist-note editing has the correct binding; faults and parts can be removed for correction; photo selection uses the .NET 10 API.
- Schedules can be edited with concurrency protection; technician profiles can be updated; stock can be adjusted with version checks and audit. Retired assets are excluded from preventive generation.
- Photo ownership/status/count decisions are serialized on the work-order row.
- Dashboard refresh updates selected inspection details. Node 24 is pinned and npm rejects other major versions.
- The live mobile-core journey is included in CI; Docker services recover after engine restart. A redesign handoff describes stable contracts and required behaviors.

## Acceptance distinction

Functional source completion and API/mobile-core acceptance are separately recorded from native device acceptance. Any native runtime limitation must remain visible in `verification.md`; an APK build alone does not establish that camera, secure storage, page lifecycle or the complete native offline journey passed. GitHub CI configuration is supplied; local equivalent checks do not establish a remote CI run.
