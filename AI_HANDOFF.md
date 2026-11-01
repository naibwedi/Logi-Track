# LogiTrack — Engineering and Operations Handoff

Last updated: 2026-11-01  
Repository: `https://github.com/naibwedi/Logi-Track`  
Production: `https://136-114-159-144.nip.io`

This document is the starting point for another AI or developer. Read it with
`README.md`, then inspect the current code before changing behavior. Never add
credentials, `.env`, database files, or Data Protection keys to Git.

## 1. What the product is

LogiTrack is a server-rendered transportation management system for a trucking
company. It covers the operating workflow from customer shipment request to
dispatch, driver execution, invoicing, settlement, and payment tracking.

It is not a public REST API or a JavaScript SPA. It is an ASP.NET Core MVC/Razor
application with cookie authentication and role-specific server-rendered pages.

### Roles

| Role | Main responsibilities |
|---|---|
| SuperAdmin | Create, edit, list, and remove Admin accounts. |
| Admin | Approve customers, manage drivers/fleet/brokers, price and assign trips, monitor compliance, prepare payments, and download invoices/settlements. |
| Driver | View assigned/current trips, simulate/report GPS progress, complete trips, review history, payments, and settlements. |
| Customer | Register, wait for approval, create shipment requests, accept/reject pricing, follow trips, review history, and download completed-trip invoices. |
| Public visitor | View marketing, services, privacy, contact, registration, and login pages. |

## 2. Technology and architecture

- .NET 8 / ASP.NET Core MVC and Razor Pages
- ASP.NET Core Identity with cookie authentication and role authorization
- Entity Framework Core 8
- SQLite in local development, automated tests, and the current VM deployment
- SignalR for authorized live trip-location updates
- Bootstrap 5 as the UI component system
- Leaflet and OpenStreetMap for maps
- PDFsharp/MigraDoc for server-generated invoices and settlements
- xUnit integration and adversarial tests
- Docker multi-stage build, Docker Compose, and Caddy reverse proxy
- Google Compute Engine deployment

Important correction: older project notes mention PostgreSQL in production, but
the current Google VM intentionally uses persistent SQLite at `/app/data/app.db`.
PostgreSQL configuration exists in the code but is not used by the live deployment.

### Request path

`Browser -> Caddy (80/443, TLS) -> ASP.NET container (5000) -> EF Core -> SQLite`

Caddy is the only publicly published container. The ASP.NET port is private to
the Compose network. `ForwardedHeadersOptions` trusts Google load-balancer ranges
and the private Docker proxy range, not arbitrary public forwarding headers.

## 3. Repository map

| Path | Purpose |
|---|---|
| `Program.cs` | Dependency registration, Identity, cookies, rate limiting, forwarded headers, middleware, migrations, seeding, health endpoint, and route mapping. |
| `Controllers/` | MVC workflows for Admin, Customer, Driver, SuperAdmin, and public Home pages. |
| `Areas/Identity/Pages/Account/` | Login and registration Razor Pages. |
| `Data/ApplicationDbContext.cs` | EF Core model, relationships, concurrency-token refresh, and database access. |
| `Data/SeedData.cs` | Roles, initial fleet/brokers, and optional first SuperAdmin creation. |
| `Models/` | Identity users, trips, driver assignments, vehicles, brokers, payments, periods, locations, and audit data. |
| `Models/ViewModel/` | Validated form and display models. |
| `Services/` | Email, sanitization, compliance, coordinates, pricing/settlements, authorization attribute, and PDF generation. |
| `Hubs/TripHub.cs` | Authorized SignalR trip groups and location updates. |
| `Views/` | Role-specific Razor views and shared layout/login partials. |
| `wwwroot/css/` | Shared navigation styling plus page-scoped role styles. |
| `wwwroot/js/` | SignalR client, maps, GPS simulation, and shared behavior. |
| `Migrations/` | EF Core schema history. |
| `tests/LogiTrack.Tests/` | Smoke, workflow, access-control, concurrency, PDF, fleet, SignalR, resilience, and stress tests. |
| `Dockerfile` | .NET 8 build and runtime image. |
| `docker-compose.yml` | ASP.NET + Caddy services, environment, and persistent mounts. |
| `Caddyfile` | Automatic HTTPS and reverse proxy. |
| `.env.example` | Required deployment-variable template; never commit the real `.env`. |

## 4. Domain model

### Identity hierarchy

`ApplicationUser : IdentityUser` is the Identity base record. `SuperAdmin`,
`Admin`, `Driver`, and `Customer` use EF inheritance. Important shared fields
include name, company, date of birth, role type, approval state, created/modified
timestamps, and soft-delete metadata. Driver adds CDL, medical-card, availability,
payment-rate/frequency, location, and vehicle-related information.

### Operations

- `Trip`: customer shipment, route, load, dates, price, coordinates, status, and
  optimistic concurrency token.
- `DriverTrip`: trip/driver/vehicle assignment, completion, settlement values,
  deductions, TONU, and concurrency token.
- `Vehicle`: unit/VIN/plate, compliance dates, operational status, and driver.
- `Broker`: MC number, contacts, notes, and blacklist status/reason.
- `Payment` and `PaymentPeriod`: prepared driver settlement and payment state.
- `Location`: driver coordinates used by live tracking.
- `AdminActionLog`: sensitive administrative action history.

### Trip lifecycle

The exact enum is in `Models/Trip.cs`. The main business flow is:

1. Customer submits a request.
2. Admin reviews it and sets a price.
3. Customer accepts or rejects the price.
4. Admin assigns a driver, vehicle, and optionally a broker.
5. Driver starts progress through SignalR/GPS simulation.
6. Driver completes the trip.
7. Admin prepares invoices, settlements, and payment records.

State-changing operations use antiforgery validation, ownership/role checks,
transactions where needed, and concurrency tokens to reject stale updates.

## 5. Backend behavior

### Startup

`Program.cs` selects the configured connection, applies migrations, creates the
four Identity roles, and runs seed data. In production, failure during migration
or required startup initialization must fail the application rather than silently
serve against a broken schema.

### Authentication and authorization

- Cookie authentication via ASP.NET Core Identity.
- Production cookies are always `Secure`, HTTP-only, and use appropriate SameSite.
- Login lockout is enabled.
- Customer registration does not grant access until an Admin approves the account.
- Email confirmation is required only when outbound email is actually configured.
- Controllers use role attributes plus record-level ownership checks.
- `ApprovedOnlyAttribute` prevents unapproved customer use.
- Destructive/state-changing actions are POST operations with antiforgery checks.

### Security controls

- Global antiforgery validation for unsafe HTTP methods.
- HTML/input sanitization at trust boundaries.
- IDOR protection for trip, invoice, location, and settlement records.
- Optimistic concurrency on `Trip` and `DriverTrip`.
- Transaction-guarded assignment/state transitions.
- Contact endpoint rate limit: 5 requests per minute per remote IP.
- Security headers: HSTS, frame denial, MIME sniffing protection, referrer and
  permissions policies.
- Known reverse-proxy ranges are explicit.
- Production secrets are environment variables and root-readable VM files only.

### Real-time tracking

`TripHub` authorizes group membership per trip. Drivers can publish only for their
own active assignment; Admins may monitor/update operational trips. Latitude and
longitude are bounds-checked. Updates are broadcast only to `trip_{tripId}`.
Leaflet views subscribe through `wwwroot/js/tripTracking.js`; the driver simulation
publishes interpolated coordinates through `driverSimulation.js`.

### Documents

`PdfInvoiceService` creates invoice PDFs for completed trips. Settlement PDFs are
also available to authorized Admin and Driver users. Always preserve ownership,
role, and completed-trip checks when changing download endpoints.

### Email

SendGrid is optional. When it is not configured:

- account approval remains required;
- email confirmation is not allowed to block all sign-ins;
- the public contact form fails closed with a clear unavailable message.

Do not fake successful email delivery.

## 6. Frontend and design system

The application uses Bootstrap components, not Material UI or Tailwind. Do not add
a second framework without a migration plan: this would duplicate CSS, increase
payload, and create specificity conflicts in Razor views.

### Current UI conventions

- Shared shell: `Views/Shared/_Layout.cshtml`
- Shared responsive navigation: `wwwroot/css/navBar.css`
- Logo asset: `wwwroot/images/logitrack-logo.png`
- Desktop logo height: 68px; phone height: 56px
- Navigation remains collapsed below the Bootstrap `xxl` breakpoint because the
  Admin role has many destinations.
- Use Bootstrap cards, alerts, badges, responsive tables, form controls, button
  groups, grid utilities, and empty-state panels.
- Use `table-responsive` around data-heavy tables.
- Use visible labels, semantic headings, captions (visible or visually hidden),
  focus states, meaningful alt text, and status regions.
- Keep role/page styles page-scoped through Razor `@section Styles`; never reload
  `Dashboard.css`, `adminDashboard.css`, or `driverDashboard.css` globally.
- The skip link and `main` landmark must remain.
- Test at 390px, 768px, 1024px, 1440px, and keyboard-only navigation.

### Recent responsive cleanup

Commit `eba3a7d` removes globally loaded role styles that previously forced a black
background, overrode all Bootstrap buttons, and introduced fixed 440–500px offsets.
It also adds responsive role cards, larger branding, collapsing navigation,
wrapping Admin forms, responsive tables, clear empty states, and Bootstrap-based
Trip Requests markup. Continue consolidating legacy CSS rather than adding more
global selectors.

## 7. Tests and verification

Run:

```powershell
dotnet restore
dotnet build --no-restore -m:1
dotnet test --no-restore -m:1
```

The last complete pre-UI suite passed 370/370 tests. The responsive changes are
Razor/CSS-only and compile with zero warnings/errors. On 2026-11-01 the local
VSTest runner later failed to establish its testhost/datacollector IPC connection;
this was an environment/tooling failure before any test executed, not a reported
test assertion failure. Re-run the full suite in a clean process or CI before the
next release and update this statement.

Important test groups include:

- application and route smoke tests;
- complete customer/admin/driver workflow;
- cross-user/IDOR and authorization tests;
- concurrency and double-submit tests;
- SignalR group/role isolation and stress;
- invoice/settlement PDF output;
- fleet, equipment, compliance, broker, and settlement behavior;
- resilience and adversarial inputs.

For every production deployment also verify:

1. `/health` returns HTTP 200 and `{"status":"healthy"}`.
2. HTTP redirects to HTTPS.
3. Login sets a Secure authentication cookie.
4. Each role lands on its own dashboard.
5. Navigation links return expected 2xx/3xx results.
6. Database and Data Protection mounts persist after container replacement.
7. Application/Caddy logs contain no new errors.

## 8. Production deployment

### Google Cloud resources

| Resource | Value |
|---|---|
| Project | `flowlingo-492120` |
| VM | `rufta-tms-server` |
| Zone / region | `us-central1-c` / `us-central1` |
| Machine | `e2-micro` (light traffic only) |
| OS | Ubuntu Minimal 22.04 LTS |
| Reserved public IP | `136.114.159.144` (`rufta-tms-ip`) |
| Current hostname | `136-114-159-144.nip.io` |
| App directory | `/opt/logitrack` |
| Compose data | `/opt/logitrack/data` |
| Data Protection keys | `/opt/logitrack/keys` |
| Deployment environment | `/opt/logitrack/.env` (mode 600, never print or commit) |

The VM has a 2 GB swap file because building the .NET image on e2-micro otherwise
risks out-of-memory termination. The boot disk is configured not to auto-delete
with the VM.

### Network

- Public: TCP 80 and 443 only.
- SSH: TCP 22 only from Google IAP range `35.235.240.0/20`, target tag `iap-ssh`.
- Public SSH and RDP rules were removed.
- Connect with `gcloud compute ssh ... --tunnel-through-iap`.

### Deployment commands

Run from the VM:

```bash
cd /opt/logitrack
sudo git pull --ff-only
sudo docker-compose build logitrack
sudo docker rm -f logitrack_logitrack_1
sudo docker-compose up -d
sudo docker-compose restart caddy
sudo docker-compose ps
curl -fsS https://136-114-159-144.nip.io/health
```

Why the explicit container replacement: this VM currently has legacy
`docker-compose` 1.29.2 with current Docker, and recreation may fail with
`KeyError: 'ContainerConfig'`. Removing only the exact application container
works without touching bind-mounted data or named Caddy volumes. A future task
should install Docker Compose v2 and switch commands to `docker compose`.

Restart Caddy after replacing the application container so it resolves the new
private container IP. TLS certificates and account data persist in named volumes.

### Environment variables

See `.env.example` and `docker-compose.yml`. Important variables are:

- `DOMAIN`
- `BOOTSTRAP_ADMIN_EMAIL`
- `BOOTSTRAP_ADMIN_PASSWORD` (first startup only; protect and remove/rotate)
- `SENDGRID_API_KEY`
- `SENDGRID_FROM_EMAIL`
- `CONTACT_RECIPIENT`

Compose maps these to ASP.NET configuration and sets persistent SQLite/Data
Protection paths. Never expose the current password in documentation or commits.

### Domain changes

The current `nip.io` hostname is temporary. For a real domain, create an A record
pointing to `136.114.159.144`, change `DOMAIN` in `/opt/logitrack/.env`, and recreate
or restart Caddy. It will request and renew HTTPS automatically. A branded temporary
host may use `logitruck.136-114-159-144.nip.io`; `logitruck.nip.io` alone does not
encode the server address.

## 9. Known limitations and next priorities

### P0 — before higher traffic or business-critical use

1. Change the initial SuperAdmin password and remove bootstrap credentials from
   the long-lived environment after verifying recovery access.
2. Configure SendGrid with a verified sender and test registration/contact flows.
3. Configure automated off-VM database/disk backups and perform a restore drill.
4. Install Docker Compose v2 and document a rollback procedure.
5. Re-run all 370 tests in clean CI after the UI commit.

### P1 — production maturity

1. Buy/connect the real domain and replace the temporary `nip.io` hostname.
2. Upgrade from e2-micro before meaningful concurrent traffic; add CPU, memory,
   latency, disk, uptime, and certificate-expiry alerts.
3. Migrate SQLite to managed PostgreSQL/Cloud SQL if write concurrency, HA, or
   horizontal scaling becomes necessary.
4. Add CI deployment with an immutable image registry rather than building on VM.
5. Add retention/rotation for application logs and audit operational dashboards.
6. Finish responsive visual regression checks for every populated and empty role
   page at phone/tablet/desktop widths.

### P2 — maintainability

1. Split the very large `AdminController` into coherent feature controllers or
   application services without changing route contracts abruptly.
2. Consolidate remaining legacy CSS and correct legacy naming such as
   `DriverManagment` only with compatible redirects.
3. Add reusable Razor partials/components for repeated page headers, empty states,
   status badges, and responsive data tables.
4. Align older `PROJECT.md` claims with the actual current deployment and tests.

## 10. Safe-change checklist for another AI

Before editing:

1. Run `git status`; preserve unrelated user changes.
2. Read this file, `README.md`, `Program.cs`, the controller, model, view, and tests
   for the feature being changed.
3. Search all callers/routes before changing names or contracts.
4. Do not weaken ownership checks, antiforgery, concurrency, sanitization, or role
   authorization to simplify a test or UI flow.
5. Prefer existing Bootstrap and service patterns; do not add a framework for a
   small styling change.

Before handing off:

1. Run build and relevant/full tests.
2. Check Razor markup, mobile overflow, keyboard focus, empty states, validation,
   authorization, and destructive-action confirmations.
3. Verify `git diff --check` and ensure no secret/database/key was added.
4. If deployed, check health, HTTPS, role login, persistence, logs, and firewall.
5. Update this document when architecture, infrastructure, or known risks change.
