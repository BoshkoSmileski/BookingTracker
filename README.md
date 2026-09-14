# BookingTracker

An appointment scheduling platform, built as a bachelor's thesis project. Organizers publish
booking pages, define when they are available, and let visitors book a slot without creating an
account. Bookings flow out to the organizer's Google Calendar and to both parties' inboxes as
email with calendar invitations attached.

Its distinguishing feature is **booking-session tracking**: every visitor interaction on the
booking form — each keystroke, date and time selection, idle/active transition, tab close,
submission, cancellation and reschedule — is recorded as an immutable event, not just the final
form values. The `BookingSessions` table is a projection of that event log, and a `/rebuild`
endpoint proves it by reconstructing a session's state purely by replaying the events and comparing
the result against the stored row.

---

## What it does

**Organizer accounts.** Registration and login with JWT access tokens (15 minutes) and rotating,
one-time-use refresh tokens (30 days). Every organizer-scoped endpoint re-checks ownership
server-side, so no organizer can reach another's data by guessing an id.

**Booking pages.** An organizer can publish several, each with its own slug, title, description,
appointment duration, before/after buffers and active flag. Per page they can also configure:

| Screen | What it sets |
|---|---|
| Duration & buffers | Appointment length, and padding either side of it |
| Booking limits | Minimum notice, how far ahead bookings are allowed, maximum bookings per day |
| Meeting | In person, or Google Meet — in which case a real join link is created per booking |
| Instructions | Guidance visitors *read* before booking |
| Booking form | Custom questions visitors *answer* while booking (short or long text, optional or required) |

**Availability.** A weekly working schedule per organizer — per-day on/off, with several intervals
in a day so a lunch break is simply a gap — stored against one IANA time zone. On top of that, two
kinds of per-date exception, both managed from one screen:

- **Blocked periods** subtract time: a whole day, a range of days, or a time window on each day of
  a range.
- **Date-specific hours** replace the weekly hours for one date, and can open a day the weekly
  schedule closes (a Saturday) or close one it opens.

Slots are generated deterministically from working hours, minus exceptions, existing bookings,
buffers, minimum notice and the booking window. A new organizer's first booking page arrives
bookable: creating it seeds a Monday–Friday 09:00–17:00 schedule when they have none yet.

**The guest booking flow.** A three-step public wizard — Time, Details, Confirm — with the calendar
and the available times on one screen, a persistent header stating what is being booked, and every
time shown on the organizer's clock with the time zone named. No account required. Guests then
manage their own booking (view, reschedule, cancel) through a link containing a long random token.

**Double-booking prevention.** An organizer cannot be booked twice at once, including across two
different booking pages. The check runs inside a `Serializable` transaction, so two guests
submitting the same slot simultaneously produce exactly one booking and one `409`. The availability
rules the slot list applies are re-checked at submit as well, so a client that never asks for the
slot list cannot book a closed day, a past date, or a slot beyond a configured limit.

**Organizer dashboard.** A live session list per booking page (Active / Submitted / Abandoned /
Cancelled), streamed over SignalR as visitors interact, plus a per-session event timeline, the
guest's details and answers, reminder status, email history, and cancel / reschedule / resend
actions.

**Analytics.** A read-only reporting dashboard, filterable by date range, booking page and status:
headline counts, booking trend, status distribution, a conversion funnel derived from the event
log, abandonment analysis, popular weekdays and hours, time-to-book, per-page performance, and
email, reminder and calendar health. Downloadable as CSV or as a printable PDF report, both
rendered from the same queries the dashboard itself calls. No new tables — every figure is derived
from data other features already write.

**Notifications.** Booking confirmation, organizer notice, cancellation, reschedule and reminders,
each a real `multipart/alternative` email (HTML and plain text) carrying an RFC 5545 `.ics`
invitation that creates, then updates, then finally cancels the same event in the recipient's
calendar. Reminders are configurable per organizer — any combination of up to eight lead times —
and are materialised as rows when a booking is confirmed, so they can be shown to the organizer,
cancelled with the booking, and regenerated when it moves.

**Google Calendar.** Two-way in the sense that matters here: the organizer's existing busy time
blocks public availability, and confirmed bookings create, update and delete events on their
calendar.

---

## Booking-session tracking

`BookingSession` is the current, materialised state of a visitor's session — the read model. Every
mutating method on it (`ChangeField`, `SelectDate`, `Submit`, `Cancel`, `Reschedule`, …) does three
things: validates a rule, builds an immutable `BookingSessionEvent` describing what happened, and
funnels it through a single private `Apply(BookingSessionEvent)` method that performs the actual
state change.

`BookingSession.Rebuild(...)` reuses that same `Apply` to reconstruct state purely by replaying a
persisted event log, and `GET /api/booking-sessions/{id}/rebuild` exposes it — so the claim that
the projection holds nothing the log cannot derive is demonstrable rather than asserted.
`BookingSessionEvent` rows are genuinely append-only (no setters, no update method, only static
factory methods), and `ClientSequenceNumber`, assigned by the browser, is what orders them.

This is also what makes the session states meaningful rather than guessed at:

- **Active** — the session exists and the visitor is still working in it.
- **Submitted** — the booking was confirmed; the session carries a booking reference and a token.
- **Abandoned** — a background sweeper marks any Active session idle for more than five minutes,
  recording a `BookingAbandoned` event rather than silently changing a column.

**Scope, stated honestly.** This is event sourcing applied to one aggregate, not a general-purpose
event-sourced architecture. There is no event-store product — `BookingSessionEvents` is an ordinary
EF Core table — and the log and its projection live in the same database and are written in the
same transaction, so there is no eventual consistency to reconcile. That was a deliberate choice at
this scale, and it still supports the property the thesis rests on, which `/rebuild` checks.

On the client, `useBookingSessionTracker` batches events and flushes them on a debounce, uses
`navigator.sendBeacon` on tab close to catch what an ordinary request cannot, and resumes an
interrupted session from `sessionStorage` after a reload.

---

## Architecture

Clean Architecture, four backend projects, dependencies pointing inward only
(**Api → Infrastructure → Application → Domain**):

```
BookingTracker.Domain          Entities, value objects, enums, the pure slot-generation service.
                               No external dependencies: no EF Core, no ASP.NET types.
BookingTracker.Application     CQRS commands/queries/handlers/validators, DTOs, and the interfaces
                               Infrastructure must implement. Depends only on Domain.
BookingTracker.Infrastructure  EF Core, SignalR, email, Google Calendar, background workers, JWT
                               and password hashing. Implements Application's interfaces.
BookingTracker.Api             Controllers, DI wiring, Program.cs, appsettings.
```

Domain and Application never reference Infrastructure or Api.

**CQRS via MediatR.** Every mutation and query is a `record` implementing `IRequest<TResponse>`,
with a sibling handler and — where there is input to check — a sibling FluentValidation validator,
which is discovered automatically and runs in a pipeline behavior before the handler ever does.
Controllers build a request and return its result; they hold no business logic and never touch the
`DbContext`.

**No repository pattern.** Handlers use `IBookingTrackerDbContext` (which exposes `DbSet<T>`)
directly: EF Core is already a repository-shaped abstraction, and the interface still keeps
Application decoupled from Infrastructure.

**Four background workers**, all `BackgroundService` — the session abandonment sweeper, the
reminder sweeper, the email queue processor, and the Google Calendar sync sweeper.

**Errors** are typed exceptions thrown by Domain and Application (`NotFoundException`,
`ForbiddenException`, `ConflictException`, `ValidationException`, …). A single middleware in the
Api layer is the only place that knows the HTTP mapping.

### Tech stack

**Backend** — .NET 8 / ASP.NET Core 8, MediatR, FluentValidation, EF Core 8 on SQL Server, SignalR,
JWT bearer authentication, ASP.NET Core Data Protection (which encrypts the stored Google OAuth
tokens), `Google.Apis.Calendar.v3`, the built-in rate limiter, Swagger in Development, and xUnit.

**Frontend** — React 19, TypeScript, Vite, React Router, Tailwind CSS 4, `@microsoft/signalr`,
`lucide-react`, oxlint, Vitest + React Testing Library, and Playwright.

Deliberately absent: no state-management library, no CSS-in-JS, no charting library (the analytics
charts are hand-rolled SVG), no mocking library in the backend tests, and no package for either
export format — the CSV and PDF writers are written against the BCL, as is the `.ics` builder.

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/) with npm
- SQL Server or [SQL Server Express](https://www.microsoft.com/sql-server/sql-server-downloads)
  running locally. The default connection string expects an instance named `SQLEXPRESS`; edit
  `ConnectionStrings:DefaultConnection` in `backend/src/BookingTracker.Api/appsettings.json` if
  yours is named differently.
- `dotnet-ef`, for the migration command below: `dotnet tool install --global dotnet-ef`

## First-time setup

```bash
git clone <repository-url> BookingTracker
cd BookingTracker

# 1. Backend: restore
cd backend
dotnet restore

# 2. Backend: set the JWT signing key. REQUIRED, and it must come BEFORE step 3 -
#    the migration command builds the API host, which refuses to start without it.
dotnet user-secrets set "Jwt:Secret" "<a long random value>" --project src/BookingTracker.Api

# 3. Backend: create the database
dotnet ef database update --project src/BookingTracker.Infrastructure --startup-project src/BookingTracker.Api

# 4. Frontend: install dependencies
cd ../frontend
npm install
```

**Step 2 is not optional, and its position matters.** `Jwt:Secret` is deliberately absent from
every `appsettings*.json` file and is never committed. It is needed before step 3 as well as at run
time: this project has no `IDesignTimeDbContextFactory`, so `dotnet ef` builds the real API host in
order to find the `DbContext`, and that host fails fast on a missing secret. Running the migration
first therefore ends in `Unable to create a 'DbContext'`, not in a created database.

At run time `Program.cs` throws a descriptive `InvalidOperationException` rather than falling back
to a generated default — which would silently make every previously issued token unverifiable and
hide a missing configuration value instead of surfacing it. In Development the value comes from
[.NET User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets), stored outside
the repository; in Production, from the `JWT__Secret` environment variable. Any long random string
works — for example, in PowerShell:

```powershell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))
```

On first run in Development, EF migrations are applied automatically and `DevelopmentSeeder` seeds
a demo organizer and a demo booking page (`demo-30-min-meeting`) so there is something to click
through immediately. The seeded login is `organizer@example.com`, and `/login` carries a **Fill
demo credentials** button that exists in development builds only and is stripped from a production
bundle — so the password is not published anywhere.

## Running the app

Two terminals:

```bash
# Terminal 1 - backend API (http://localhost:5216, Swagger at /swagger)
cd backend
dotnet run --project src/BookingTracker.Api

# Terminal 2 - frontend (http://localhost:5173)
cd frontend
npm run dev
```

Then open `http://localhost:5173`:

| URL | |
|---|---|
| `/` | Landing page |
| `/login`, `/register` | Organizer sign-in and sign-up |
| `/dashboard` | The organizer workspace: booking pages, availability, settings, analytics |
| `/book/demo-30-min-meeting` | The public booking wizard, as a visitor sees it |
| `/manage/{token}` | A guest's own booking, reached from the confirmation email |

---

## Configuration

Everything lives in `backend/src/BookingTracker.Api/appsettings.json` (and the
`appsettings.Development.json` override beside it), except secrets.

**Required:**

| Setting | |
|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server. Defaults to `localhost\SQLEXPRESS`, database `BookingTracker` |
| `Jwt:Secret` | The token signing key. **Not in any config file** — User Secrets in Development, `JWT__Secret` in Production. See above |

**Optional — each has a working default:**

| Section | |
|---|---|
| `Jwt:Issuer` / `Audience` / `AccessTokenMinutes` / `RefreshTokenDays` | Token issuance. Defaults 15 minutes and 30 days |
| `Cors:AllowedOrigins` | Defaults to `http://localhost:5173`. Credentials are allowed, which SignalR requires |
| `Frontend:BaseUrl` | Used to build the links in outgoing email |
| `RateLimiting:Authentication` / `Booking` | Per-IP fixed windows on the anonymous endpoints. Defaults 5/min and 60/min |
| `Email:*` | See below. `UseSmtp` is `false` by default, so nothing is sent |
| `Reminders:*` | Sweep interval, batch size, grace period and scan window for the reminder worker |
| `GoogleCalendar:ClientId` / `ClientSecret` / `RedirectUri` | Blank by default; the integration is simply unavailable until filled in |

The application runs fully without Google credentials and without SMTP. Neither is needed to
develop, demo, or run any test suite.

The frontend reads one optional variable, `VITE_API_BASE_URL`, which defaults to
`http://localhost:5216` (`frontend/src/lib/config.ts`). No `.env` file is required or shipped;
set the variable only if the API is served from somewhere else.

### Email in development

`Email:UseSmtp` is `false` by default, so no mail is transmitted. `FileSystemEmailService` writes
each message to `%TEMP%\BookingTracker\sent-emails\` as `.html`, `.txt` and — where the
notification carries an invitation — `.ics`, so the exact output is inspectable without an SMTP
account. **A missing email is almost always this**, not a failure: the queue, the worker and the
event log are all doing their job.

To send real mail, set `Email:UseSmtp` to `true` and supply a host. Credentials are secrets, so
they belong in User Secrets rather than in a tracked config file:

```bash
cd backend
dotnet user-secrets set "Email:UseSmtp"  "true"             --project src/BookingTracker.Api
dotnet user-secrets set "Email:Host"     "smtp.gmail.com"   --project src/BookingTracker.Api
dotnet user-secrets set "Email:Username" "you@example.com"  --project src/BookingTracker.Api
dotnet user-secrets set "Email:Password" "<app password>"   --project src/BookingTracker.Api
dotnet user-secrets set "Email:FromEmail" "you@example.com" --project src/BookingTracker.Api
```

Two provider notes: Gmail needs an App Password rather than the account password, and `FromEmail`
should match `Username` or the provider will rewrite or reject the sender. Startup fails fast with
a message naming the missing key if `UseSmtp` is `true` but `Host` or `FromEmail` is blank — a
local relay such as MailHog or smtp4dev needs no username or password at all.

Nothing is sent from a request thread. Handlers queue an `EmailNotification` row and
`EmailQueueProcessor` drains it in the background, retrying with exponential backoff and marking a
message permanently failed after `Email:MaxRetries` attempts — so a slow or unreachable SMTP server
can never delay or fail a booking.

### Google Calendar (optional)

1. In the [Google Cloud console](https://console.cloud.google.com), enable the **Google Calendar
   API**.
2. On the **OAuth consent screen**, add the `https://www.googleapis.com/auth/calendar` scope, and
   add every Google account that will connect as a **Test user** — while the app is in Testing
   status, any account not on that list is refused with a verification error at Google's own
   consent screen, before the request ever reaches this application.
3. Create an **OAuth client ID** of type *Web application* with the redirect URI
   `http://localhost:5216/api/calendar/google/callback`.
4. Put the client ID and secret into User Secrets. A client secret is a credential, so — like the
   SMTP password above — it must not go into a tracked `appsettings*.json` file:

```bash
cd backend
dotnet user-secrets set "GoogleCalendar:ClientId"     "<client id>"     --project src/BookingTracker.Api
dotnet user-secrets set "GoogleCalendar:ClientSecret" "<client secret>" --project src/BookingTracker.Api
```

Leaving them unset is fine: `GoogleCalendar:ClientId` / `ClientSecret` are blank in
`appsettings.json` and the integration is simply unavailable until they are filled in. Nothing else
in the application requires them.

Access and refresh tokens are encrypted at rest with the ASP.NET Core Data Protection API; the
keys are persisted to `%TEMP%\BookingTracker\dataprotection-keys`, so deleting that folder makes
existing stored tokens permanently undecryptable.

### Security and configuration

No credential belongs in a tracked file. The three this application can use are all supplied out of
band, and none of them appears in any `appsettings*.json`:

| | Development | Production |
|---|---|---|
| `Jwt:Secret` | .NET User Secrets | `JWT__Secret` environment variable |
| `Email:Password` | .NET User Secrets | `Email__Password` environment variable |
| `GoogleCalendar:ClientSecret` | .NET User Secrets | `GoogleCalendar__ClientSecret` environment variable |

`__` is `IConfiguration`'s cross-platform separator for `:` in an environment variable name. User
Secrets live outside the repository, so nothing in that list can be committed by accident.
`appsettings.json` carries each of these keys as an empty string: it is the *shape* of the
configuration, not a place to fill one in. `.gitignore` additionally excludes `.env` files,
`appsettings.Local.json`, `appsettings.Production.json`, SQL Server data files (`.mdf`/`.ldf`) and
`TestResults/`.

Startup fails fast rather than degrading quietly: a missing `Jwt:Secret` stops the application, and
so does `Email:UseSmtp` being `true` with no `Host` or `FromEmail`. A missing credential is a
refusal to start, never a silent misbehaviour discovered later.

**If you fork, redeploy, or otherwise reuse this project, generate your own `Jwt:Secret` and your
own Google OAuth client.** A signing key copied from anywhere else is not a secret, and a client
secret that has ever been committed to a repository stays compromised until it is rotated in the
Google Cloud console — removing it from the working tree does not un-publish it.

---

## Testing

Six suites: four backend (xUnit), one frontend (Vitest), and one browser suite (Playwright). Counts
below are a snapshot at the time of writing, not a contract.

```bash
# Backend - all four projects via the solution
cd backend
dotnet test

# ...or one at a time. The first two need no infrastructure at all:
dotnet test tests/BookingTracker.UnitTests           # 585  domain, handlers, services
dotnet test tests/BookingTracker.IntegrationTests    # 211  real HTTP, in-memory database
dotnet test tests/BookingTracker.SqlServerTests      #  28  needs SQL Server Express
dotnet test tests/BookingTracker.MigrationTests      #  31  needs SQL Server Express
                                                     #      (12 upgrade + 19 rollback)

# Frontend - components, hooks, pages
cd frontend
npm test              # 824 tests
npm run build         # tsc -b (which typechecks the tests too) + vite build
npm run lint          # oxlint
```

Each suite exists for something the ones below it cannot reach:

- **UnitTests** — the business logic, with no I/O: slot generation across working hours, buffers,
  exceptions, time zones and notice periods; the event-sourcing claim itself (`Rebuild()`
  reconstructing identical state for every event type); auth; the email queue's retry policy;
  reminders; analytics; ICS formatting; both export writers.
- **IntegrationTests** — everything decided *between* components: model binding, status codes,
  validation response shape, auth boundaries. Real `Program.cs` over `WebApplicationFactory`, real
  signed JWTs, an in-memory database, no email and no Google. The motivating case is an enum bound
  as its own type rather than as a string: every handler test passed while a real HTTP request was
  rejected.
- **SqlServerTests** — what an in-memory provider cannot represent. It is the only place the
  `Serializable` transaction behind double-booking actually executes: a genuine concurrent race at
  2, 4, 8 and 16 simultaneous bookers, plus the unique and filtered indexes, real column limits and
  cascade deletes. Writing it found a real bug — the guest who lost a race was answered `500`
  instead of `409`.
- **MigrationTests** — whether a database that already holds rows written by an *older* version can
  be upgraded. It builds a database at an old migration, populates it, runs the production
  `MigrateAsync()`, and then checks the rows, the one data backfill in the history, the resulting
  schema and the running application. On a second database it does the reverse, driving the schema
  back down one `Down()` migration at a time and up again — classifying each rollback rather than
  pretending rollback is lossless, since most of them destroy information by definition.

### Browser end-to-end tests

```bash
cd frontend
npx playwright install chromium   # once per machine
npm run test:e2e                  # 29 tests, headless
npm run test:e2e:ui               # Playwright's interactive UI
npm run test:e2e:reset            # drops and recreates the E2E database first
```

A real Chromium against a real API and a real database, for the two things nothing else can reach:
jsdom has no server and applies no CSS, and `WebApplicationFactory` has no browser. It covers the
full journey (register → publish → a guest books on that page), session and redirect behaviour,
the public booking and manage flows, API-failure states, routing, layout geometry at 375px and
1440px, and keyboard and skip-link accessibility.

`npm run test:e2e` does everything itself — it migrates a separate `BookingTracker_E2E` database,
publishes the API to `backend/artifacts/e2e-api`, and starts both that API (port 5299) and its own
Vite server (port 5199). Nothing needs to be running beforehand, and because it uses its own ports
and its own Release build output, it runs alongside `dotnet run` and `npm run dev` on 5216/5173. It
uses a redirected `%TEMP%`, no SMTP and blank Google credentials, and before any test runs
`e2e/global-setup.ts` checks that the seeded demo organizer **cannot** sign in — that account
exists only in the development database, so a successful login would mean the API is pointed at the
wrong one, and the run aborts.

### Which database each suite uses

Every SQL-backed suite creates, proves and drops its **own** throwaway database, and refuses by
name to run against any other:

| Database | Owner |
|---|---|
| `BookingTracker` | your development database — **never touched by any test** |
| `BookingTracker_E2E` | the Playwright browser suite |
| `BookingTracker_SqlTests` | the SQL Server integration suite |
| `BookingTracker_MigrationTests` | the migration upgrade suite |
| `BookingTracker_MigrationDownTests` | the migration rollback suite |
| `BookingTracker_MigrationVolumeDiagnostics` | the opt-in volume diagnostics |

Each guard names and refuses the others explicitly, and each fixture additionally asserts at
runtime that its freshly created database is empty before writing anything — a configured name only
proves what was asked for, not what the server handed back. Point a suite at a different *server*
with `SQLTESTS_CONNECTION_STRING`, `MIGRATIONTESTS_CONNECTION_STRING`, `DOWNTESTS_CONNECTION_STRING`,
`MIGRATIONVOLUME_CONNECTION_STRING` or `E2E_CONNECTION_STRING`; the database name is still checked.

### Opt-in database diagnostics

Both SQL-backed projects also carry measurements that are skipped unless one environment variable
is set. They produce evidence about SQL Server rather than guarding a regression, they are far
slower than everything around them, and they are deliberately not part of `dotnet test` or of CI:

```powershell
$env:BOOKINGTRACKER_RUN_DIAGNOSTICS = "1"

# Query plans, logical reads, lock footprints, booking-contention latency.  (7 tests)
dotnet test tests/BookingTracker.SqlServerTests -c Release --filter "FullyQualifiedName~Diagnostics"

# What each migration costs on a database that already holds rows:
# 10,000 / 100,000 / 500,000 booking sessions, one migration at a time.     (3 tests, ~3 min)
dotnet test tests/BookingTracker.MigrationTests -c Release --filter "FullyQualifiedName~Diagnostics"
```

The migration diagnostics answer what the correctness suite cannot: proving a migration is *right*
on fifty rows says nothing about whether it is *safe to deploy* on a real one. Each migration is
applied on its own and watched from a second connection, reporting elapsed time, server-side
CPU/reads/writes, the locks it held, the transaction log it generated, and whether an ordinary
application query was **blocked** and for how long — read from SQL Server's own DMVs rather than
inferred from timing. They use a sixth throwaway database and allocate roughly 690 MB at the
largest volume.

---

## Project structure

```
backend/
  src/
    BookingTracker.Domain/          Entities, value objects, enums, SlotGenerationService
    BookingTracker.Application/     CQRS handlers, DTOs, validators, interfaces, analytics + exports
    BookingTracker.Infrastructure/  EF Core, migrations, SignalR, email, calendar, background workers
    BookingTracker.Api/             Controllers, Program.cs, middleware, appsettings
  tests/
    BookingTracker.UnitTests/       Domain and handler tests
    BookingTracker.IntegrationTests/ Real HTTP over WebApplicationFactory
    BookingTracker.SqlServerTests/  Concurrency, constraints and cascades on real SQL Server
    BookingTracker.MigrationTests/  Populated-database upgrade, and Down-migration rollback
frontend/
  src/
    components/    Design system (ui.tsx), shell, booking wizard, analytics charts
    pages/         One file per route
    hooks/         useBookingSessionTracker (event capture), useUnsavedChanges, useAvailableSlots
    lib/           Typed API client, DTO mirrors, date/time helpers, SignalR connection
    test/          Shared test infrastructure (never shipped)
    **/__tests__/  Tests, co-located with the code they cover
  e2e/             Playwright browser suite
```

---

## Known limitations

- **Google Calendar is the only calendar provider implemented.** The integration is built behind a
  provider abstraction (`ICalendarProvider`, selected by a `CalendarProviderType` enum) so that
  Outlook or Apple would be a new Infrastructure class and one enum value with no Application-layer
  change — but neither is built.
- **Calendar sync is one-way for edits.** Bookings made here are pushed to Google, and Google's
  busy time is read back to block availability, but an edit made directly on the Google event does
  not flow back into BookingTracker. That needs push notifications or polling.
- **Google Meet is the only online-meeting option**, and it requires a connected Google Calendar,
  since the join link is created as part of the same calendar-event insert. A page set to Google
  Meet whose organizer has not connected a calendar still takes bookings — they simply have no
  meeting link.
- **One organizer per booking page.** No team, round-robin or collective booking pages.
- **No payments.**
- **Development-only convenience is exactly that.** Migrations are applied and the demo data seeded
  automatically only when `ASPNETCORE_ENVIRONMENT` is `Development`; a production deployment is
  expected to run migrations as its own step. Two application instances also cannot start
  simultaneously against one empty database — the unique indexes keep the data correct, but the
  loser fails to start.
- **A booking made before its organizer connected a calendar gains no calendar event
  retroactively** unless it is rescheduled. There is no backfill sweep.
- **The PDF export is limited to the Windows-1252 character repertoire** (accented Latin degrades
  to its base letter; Cyrillic, Greek and CJK become `?`), because it embeds no font. The CSV
  export is UTF-8 and carries any script intact.
- **Deployment is not covered here.** The project is set up to be run locally for development and
  demonstration; there is no Dockerfile, reverse-proxy configuration or deployment pipeline.
- **Real Google OAuth and real SMTP delivery were never exercised end to end.** Both integrations
  are implemented and covered by automated tests, but against substitutes: the calendar tests drive
  a fake `ICalendarProvider` rather than Google, and the email tests use the development file sink
  and a scripted local SMTP server rather than a real provider. Connecting a live Google account
  requires interactive consent and sending real mail requires a real mailbox, so neither was
  performed. What this README describes for those two features is the implemented behaviour and the
  tests around it — not a verified live connection.

---

## Contributing

Work on a branch and open a pull request rather than pushing to `main`:

```bash
git checkout -b your-name/short-description
# ...changes...
git commit -m "Describe the change"
git push -u origin your-name/short-description
```

Before opening it, run `dotnet build` and `dotnet test` in `backend/`, and `npm run build`,
`npm test` and `npm run lint` in `frontend/`. `npm run build` runs `tsc -b` first, which typechecks
the tests along with the application, so a broken test type fails the build.

A few things worth knowing before changing anything, each of which has bitten this codebase:

- **New facts about a booking session must be modelled as events.** Add a `BookingEventType` value
  and a case in `BookingSession.Apply()` — never a bare field mutation outside it. `Apply` drives
  both live state and the `/rebuild` replay, so it is the single place the event-sourcing guarantee
  lives.
- **Raw event rows are organizer-only.** The one route that serves them is authenticated and
  ownership-checked, and `GetBookingSessionTimelineQuery.RequestingOrganizerId` is required and
  non-nullable so the check cannot be skipped by omitting an argument. `BookingSubmitted.NewValue`
  is the booking's `PublicToken`, which the event mapper redacts on the way out.
- **`GET /api/booking-sessions/{id}` is anonymous on purpose** and returns the guest's own details:
  it is how the wizard restores a half-filled form after a reload, and the guest holds no
  credential at that point. Adding `[Authorize]` there breaks the resume flow.
- **A rule the product advertises must be enforced where the write happens**, not only on the read
  endpoint that displays it. `BookableSlotGuard` re-checks availability at submit by dispatching
  the same query the slot list uses, rather than re-deriving any rule.
- **Every user-editable field needs its limit in three places** — the EF configuration, the
  validator, and the domain invariant — all sourced from one constant. A `SqlException` reaching
  the client is always a bug.
- **EF Core owned types are tracked by reference.** Never reuse the same record instance across two
  owners; clone with `with { }` first.
- **Any new anonymous endpoint needs an explicit rate-limit policy.** The default for a new action
  is unlimited.
