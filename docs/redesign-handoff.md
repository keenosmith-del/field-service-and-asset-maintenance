# UI redesign handoff

The Angular dashboard and native MAUI app use the same persisted API and inspection contracts. Keep the existing business workflows and synchronization engine while replacing presentation, navigation, and styling.

## Boundaries

- `web/src/main.ts`: supervisor forms, typed HTTP requests, session state, refresh, attachment loading and CSV export. `web/src/app.html` and `web/src/styles.css`: presentation.
- `mobile/FieldService.Mobile`: native XAML pages and MVVM commands. Camera, photo selection, secure session storage and native share remain real platform integrations.
- `mobile/FieldService.Mobile.Core/OfflineStore.cs`: SQLite persistence, durable outbox, photo cache, immutable retries, acknowledgments and explicit conflict resolution. Preserve this engine during redesign.
- `shared/FieldService.Contracts/Contracts.cs`: API/mobile contracts. Angular interfaces mirror these fields.
- `api/FieldService.Api`: authorization, validation, maintenance lifecycle, serializable submission transaction, stock consumption, receipts, audit and PostgreSQL migrations.

## Connected supervisor actions

| Screen | API contracts and actions |
| --- | --- |
| Sign-in | `POST /api/auth/login`, supervisor role, tab-scoped session, expiry/401 handling |
| Overview | `GET /api/reports`, persisted work orders and metrics |
| Sites | `GET/POST /api/sites`, `PUT/DELETE /api/sites/{id}` |
| Assets | `GET/POST /api/assets`, `PUT/DELETE /api/assets/{id}`, `GET /api/assets/{id}/history` |
| Technicians | `GET/POST /api/technicians`, `PUT /api/technicians/{id}` for name/email, assignments from work orders |
| Schedules | `GET/POST /api/schedules`, `PUT /api/schedules/{id}`, versioned pause/enable, `POST /api/schedules/generate` |
| Work orders | `GET/POST /api/work-orders`, `GET /api/work-orders/{id}`, versioned assignment and supervisor review |
| Inspection details | Checklist, field notes, faults, parts, start/submission/completion times, authorized photographs, `GET /api/audit/{id}` |
| Faults | `GET /api/faults`, corrective creation with `followUpFaultId`, link returned in `WorkOrderDto` |
| Reports | Persisted assignment/completion/overdue/fault metrics, actual part consumption, CSV export, versioned `PUT /api/parts/{id}/stock` |

## Preserve these behaviors

1. Starting a job, notes, checklist changes, faults, quantities, photos and submission work offline and survive restart. A queued submission locks editing until acknowledged or explicitly resolved.
2. Local `Pending`/`Syncing`/`Failed`/`Conflict` states describe delivery. Server `Submitted` requires supervisor review before `Completed`. Never display local submission as confirmed server completion.
3. Existing server photographs are cached for offline viewing. Locally created photos are referenced atomically with their outbox entry and uploaded using stable IDs before the corresponding inspection request.
4. A request ID and payload are frozen before transmission. Network failure or a lost response must retry that exact request. Later edits do not mutate a frozen request.
5. A definite rejection or stale/changed assignment retains the local draft. Show server/local comparison, then explicitly adopt the server or keep a local draft for editing. Keeping local archives the prior snapshot and clears the submission timestamp; the technician must submit the corrected draft again.
6. Parts and faults can be removed and entered again before submission. Supervisor inventory adjustments are versioned and audited, including concurrent stock consumption.
7. Server adoption retains archived local evidence. Native export includes the current draft, archives and photo bytes.
8. Schedule edits apply to future generation; existing occurrences retain their original checklist and assignment. Paused schedules and retired assets do not generate work. Occurrence uniqueness prevents duplicates.
9. Every primary screen uses real HTTP and PostgreSQL records. Preserve error states; a failed request must not display a success notice. Refresh work-order details as well as summary lists.

## Repeatable acceptance checks

```sh
./scripts/setup.sh
docker compose up -d --build
docker build -f infra/Dockerfile.tests -t fieldservice-tests .
python3 scripts/check-workflow.py
./scripts/check-mobile-journey.sh
cd web
nvm use
npm ci
npm run build
```

The mobile journey check uses the **actual SQLite sync engine against the real API and PostgreSQL**. Its transport deliberately disconnects or loses a response after the server commits; persistence and acknowledgment handling are real. It also checks supervisor review before receipt recovery, exact photo bytes, stock rollback on rejection, explicit correction, completed history, reports, audits and corrective linkage.

Run the native offline procedure in the README when changing XAML, page lifecycle, camera/picker, secure storage or connectivity behavior. Automated core tests cover the persistence engine; native acceptance checks cover platform integration. See `verification.md` and `requirements-signoff.md` for the final evidence and remaining environment limitations.
