# Field Service & Asset Maintenance

Angular supervisor dashboard, ASP.NET Core API with PostgreSQL, and a native .NET MAUI technician app with a durable SQLite outbox. This repository implements the maintenance journey from registration and dispatch through offline inspection, synchronization, supervisor review, and asset history.

## Start the API and dashboard on macOS

Prerequisites: Docker Desktop running, Node.js 24, npm, and Python 3 for integration checks. A host .NET SDK is **not** needed to run the API in Docker.

```sh
cd "/Users/keenosmith/Expansion Projects/Asset Maintenance Management/field-service-and-asset-maintenance"
./scripts/setup.sh
# Creates .env with random database, JWT and demo passwords. Existing .env is preserved.
docker compose up -d --build
curl --fail http://localhost:8080/health
cd web
npm ci
npm start
```

Open [the dashboard](http://localhost:4200). `./scripts/start.sh` runs the same setup and leaves the dashboard in the foreground. Stop the dashboard with Ctrl-C; stop infrastructure with `docker compose down` from the repository root. PostgreSQL data and attachment bytes survive container recreation in the `database` volume. PostgreSQL is exposed on **127.0.0.1:55432**, API on **8080**, Angular on **4200**. These ports avoid the existing PostgreSQL service on this development machine.

If the shell selects an older Node installation, use `nvm use` from the project directory before `npm ci` and `npm start`. If port 4200 is occupied, run `npm start -- --port 4201` and open http://localhost:4201. Angular's development proxy sends `/api` requests to port 8080; production deployment must route `/api` to the API and serve the dashboard's `web/dist/dashboard/browser` files.

### Demo credentials

| Role | Email | Password |
| --- | --- | --- |
| Supervisor (Angular) | `supervisor@fieldservice.local` | `.env` → `DEMO_PASSWORD` |
| Technician (MAUI) | `technician@fieldservice.local` | same `DEMO_PASSWORD` |

Read the generated password locally:

```sh
sed -n 's/^DEMO_PASSWORD=//p' .env
```

The seed creates a site, pump asset, assigned checklist inspection and two stocked parts. Seeding runs only when enabled and the user table is empty. Changing `DEMO_PASSWORD` after the first startup does **not** change existing accounts. Additional technician accounts can be created in Angular. Passwords are hashed with ASP.NET Identity; no shared secret or demo password is committed.

## Run the genuine MAUI Android application

Install the **.NET 10 SDK**, **JDK 21**, and **Android Studio** on macOS. In Android Studio's SDK Manager install the Android SDK and emulator, then create and start an emulator (ARM64 image on Apple Silicon). The exact Android API/build-tools versions are determined by the installed .NET Android workload; the dependency target below installs its required SDK components. No Android Studio project is substituted for MAUI.

```sh
# Repository root
sudo dotnet workload install maui-android
export JAVA_HOME=$(/usr/libexec/java_home -v 21)
export ANDROID_HOME="$HOME/Library/Android/sdk"
dotnet build mobile/FieldService.Mobile -t:InstallAndroidDependencies \
  -f net10.0-android -p:AndroidSdkDirectory="$ANDROID_HOME" \
  -p:JavaSdkDirectory="$JAVA_HOME" -p:AcceptAndroidSdkLicenses=true
# With an Android emulator already running:
dotnet build mobile/FieldService.Mobile -t:Run -f net10.0-android \
  -p:AndroidSdkDirectory="$ANDROID_HOME" -p:JavaSdkDirectory="$JAVA_HOME"
```

Choose API URL **`http://10.0.2.2:8080/`** in the Android sign-in screen (the default). `10.0.2.2` reaches the Mac's loopback network from the Android emulator. Connectivity detection also requires the API to be reachable; an Internet flag alone does not indicate successful synchronization.

Android Debug builds allow HTTP only for the emulator host and localhost. Release builds reject cleartext HTTP. Do not disable certificate validation. For a physical device or a deployed environment, use a trusted HTTPS endpoint, configure its URL at sign-in, and ensure network routing/firewalls allow it. Emulator HTTP is intended only for local development. See [Microsoft's MAUI local networking guidance](https://learn.microsoft.com/en-us/dotnet/maui/data-cloud/local-web-services?view=net-maui-10.0).


### Install the exported Android APK

The local build produced a genuine debug APK in `artifacts/android`. With Android Studio's emulator running, install it without needing to rebuild:

```sh
export ANDROID_HOME="$HOME/Library/Android/sdk"
"$ANDROID_HOME/platform-tools/adb" install -r artifacts/android/com.fieldservice.maintenance-Signed.apk
```

You can rebuild/export it in Docker using the supported x86_64 Linux toolchain (emulated on Apple Silicon; the first build downloads the SDK and workloads):

```sh
docker build -f infra/Dockerfile.android --target package \
  --output type=local,dest=artifacts/android .
```

This is a development-signed APK for local testing. Installing and executing it on an emulator remains a separate verification step.

### Optional Apple targets

The project contains native iOS and Mac Catalyst entry points, privacy descriptions and local-network transport configuration. They require compatible Xcode, Apple provisioning and .NET workloads:

```sh
sudo dotnet workload install maui-ios maui-maccatalyst
dotnet build mobile/FieldService.Mobile -p:EnableAppleTargets=true -f net10.0-ios
# iOS simulator API URL: http://localhost:8080/
```

Apple targets are optional and have not been compiled or run here. Android is the primary demonstration target.

## Demonstrate offline execution

1. Sign in to Angular. Create a **Site**, then an **Asset** with its identifier, category, location, status and service interval.
2. Under **Schedules**, select the asset, enter an interval, next due date, technician and one required checklist item per line. Save, then **Generate next 30 days**. Alternatively create a one-off work order. Inspect the assignment in **Work orders**.
3. Sign in to MAUI with the technician account. **Download jobs**, open the assignment and **Start job**.
4. Disconnect the emulator from networking. Complete checklist items and notes, add a fault, select/capture a JPEG or PNG (maximum 5 MB), and record part quantities. Notes/checklist edits autosave to SQLite after start; buttons save faults, parts and photos transactionally.
5. **Submit completed inspection** while offline. The job shows **Pending**, rather than reporting successful delivery. Force-stop and reopen the app without clearing its data. Secure storage restores the account, SQLite restores the inspection, outbox and photos.
6. Restore networking. Connectivity changes trigger sync; the app also checks every 30 seconds with backoff and exposes **Sync / retry** for immediate manual retry. Each dependency and mutation must be acknowledged before the local job becomes **Synced**. The server status is **Submitted** until review.
7. Refresh Angular. Open the submitted job, inspect checklist responses, photographs, faults, parts and timestamps. Enter review notes and **Review & complete work order**. Asset **History**, **Faults**, **Reports** and the audit trail now reflect persisted completion. Faults can create linked corrective work orders.

To demonstrate a conflict, download an assigned job, work offline, then change its assignment in Angular (including assigning it back to the same technician to increment its version). Reconnect. The local draft remains in **Conflict**. In the job page choose **Compare local and server**, then **Choose conflict resolution**, inspect the two versions, then explicitly adopt the server or rebase the retained local draft. Every resolution archives the local snapshot. Closed or reassigned work cannot be replayed; retain/export the local draft and contact the supervisor. The app can export retained JSON, including photo bytes and archived drafts, via the native share sheet. Keeping local work makes it editable, clears its submission timestamp, and requires a fresh explicit submission after correction. A definitively rejected submission (for example insufficient stock) uses this same comparison/correction path.

## Synchronization guarantees and boundaries

- Work orders have optimistic versions. Assignment changes and reviews increment them. Technician mutations require current ownership and a matching base version.
- Local drafts, outbox operations and photo bytes live in SQLite databases isolated per signed-in user. Signing out preserves those databases; secure tokens live in MAUI SecureStorage. Restore local work by signing in as the same technician.
- Only unsent outbox snapshots are coalesced. A payload is frozen and durably saved before its first network request. Retries use identical operation IDs and payloads, including after process death. Later operations acquire the last acknowledged server version.
- Photos have stable IDs and hashes. Binary JPEG/PNG upload acknowledgments are persisted locally. Inspection submission waits for its photos. Server attachment bytes are stored in PostgreSQL to keep upload persistence independent of container filesystems.
- A serializable server transaction includes the inspection, stock deductions, consumption rows, submitted faults, audit record and exact acknowledgment receipt. Replaying a persisted receipt cannot consume stock twice. Reusing an operation ID with a changed payload is rejected.
- Network/serialization failures retain pending work. Retries have bounded exponential backoff with jitter. 409/403 responses require explicit resolution. Definitive validation failures also require explicit comparison/correction, retaining the original snapshot. An expired token requires online sign-in again; local work remains intact.
- Download reconciliation updates clean jobs and never overwrites pending drafts. Frozen requests are allowed to recover lost acknowledgments before deciding a version changed. Missing assignments stay locally retained and visibly conflicted.
- State transitions are `Scheduled → Assigned → In Progress → Submitted → Completed`. An offline complete inspection may submit directly from Assigned with its captured start/submission timestamps. Only a supervisor can complete Submitted work. Parts are consumed **on submission**, not review; stock shortages reject the entire submission.
- Explicit rebase replaces the server inspection with the local snapshot. It is not automatic field merging. The comparison screen, confirmation and local archive make that choice reviewable. Reassignment and closure remain authoritative.
- Schedule generation is an explicit supervisor action, with a 30-day horizon and at most 100 overdue occurrences per schedule per invocation. Unique occurrence constraints and transactional cursor updates prevent duplicate generation. There is no background scheduling service.

## Redesign handoff

[Requirements sign-off](docs/requirements-signoff.md) records the functional checklist and evidence. [UI redesign handoff](docs/redesign-handoff.md) maps every primary screen to its API and identifies the synchronization behavior to preserve. [Verification](docs/verification.md) separates local checks from native/remote CI acceptance.

## Verification

```sh
# C# business rules and SQLite durability checks (no MAUI workload required)
dotnet test tests/FieldService.Tests -c Release
# Or run inside Docker without a host SDK:
docker build -f infra/Dockerfile.tests -t fieldservice-tests .

# Real HTTP/API/PostgreSQL checks with the stack running:
python3 scripts/check-workflow.py
# Actual mobile SQLite engine against the live API/PostgreSQL:
./scripts/check-mobile-journey.sh

# Angular production bundle:
cd web
npm ci
npm run build
```

The HTTP verification creates uniquely named `CHECK-*` sites, assets, technicians, work orders, faults and audits in the local demo database. These are retained as reviewable evidence. It checks authentication, RBAC, assignment ownership, safe photo upload/replay, required checklist validation, exact receipt replay, operation-ID collisions, stock deduction exactly once, stale versions, review permissions, completion history, reporting and repeated schedule generation.

SQLite tests exercise actual files with a deterministic HTTP test handler, including network loss, restart recovery, photograph persistence, lost server acknowledgments, exact frozen replay and explicit conflict resolution. They do not substitute for a native device test. GitHub Actions builds Angular, runs C# tests, exercises Docker/PostgreSQL integration, and compiles Android.

### Verification limitations

The API and Angular have been compiled here, and the database workflow and SQLite tests have been exercised. The host has no .NET SDK or Xcode. The real Android APK is built in Docker; Temporary emulator tooling has been removed; the APK is retained. Emulator acceptance is optional and was deferred at the user's direction for this checkpoint. Native acceptance status and any remaining platform/tool limitations are recorded precisely in `docs/verification.md`. Apple platforms remain unverified.

This is a local hands-on implementation, with deployment configuration left to the environment: configure trusted HTTPS, rotate signing keys, disable demo seeding, and provision production accounts before deployment. There is no refresh-token/password-reset system, push dispatch, automated schedule runner, or per-field conflict merging. Browser tokens are tab-scoped session storage with a 12-hour expiry; mobile tokens are secure storage with the same expiry. Parts references include seeded stock; supervisors can adjust remaining inventory from Reports, with optimistic version checks and an audit record.

## Structure and migrations

- `api/FieldService.Api`: authenticated API, lifecycle rules, EF Core model, migration and seed.
- `shared/FieldService.Contracts`: JSON contracts shared by API and MAUI.
- `mobile/FieldService.Mobile`: XAML/MVVM pages, connectivity, device camera/picker/share and secure storage.
- `mobile/FieldService.Mobile.Core`: platform-independent SQLite draft/outbox and HTTP sync engine.
- `web`: connected Angular administration screens and CSV export.
- `tests`, `scripts`, `infra`, `.github/workflows`: verification and local infrastructure.

The API applies committed EF migrations at startup. The initial migration contains readable PostgreSQL DDL, foreign keys, unique identifiers/occurrences, stock/quantity checks, receipt primary keys, concurrency versions and query indexes. The generated model snapshot supports subsequent migrations:

```sh
dotnet tool install --global dotnet-ef --version 10.0.0
dotnet ef migrations add YourChange --project api/FieldService.Api
```

`AppDbFactory` supports design-time scaffolding without loading runtime JWT secrets. To run the API outside Docker, set `ConnectionStrings__Database` with host `localhost` and port `55432`, `Jwt__Key` (at least 32 random characters), `ASPNETCORE_URLS=http://0.0.0.0:8080`, and optionally `Seed__Enabled`/`Seed__Password`. ASP.NET reads environment variables; it does not automatically load `.env`. `compose.yaml` maps `.env` values to those variables.
