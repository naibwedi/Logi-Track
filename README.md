# Logi-Track

Full-stack logistics and transport-management platform. Customers request trips,
admins price and assign them to drivers, and every party follows the trip status
in real time. Built as a university project and structured like a production
ASP.NET Core application — layered services, role-based access, migrations,
API docs, and containerised deployment.

<!-- Add a screenshot at docs/screenshot.png and uncomment:
![Logi-Track dashboard](docs/screenshot.png)
-->

## Features

- **Four roles** with separate dashboards — SuperAdmin, Admin, Customer, Driver
- **Trip lifecycle** — request → price set → driver assigned → in transit → delivered
- **Real-time updates** via SignalR (`TripHub`): status changes, price updates, and
  driver assignment push to everyone watching a trip; admin dashboard stats update live
- **ASP.NET Identity** auth with email confirmation and role seeding on startup
- **Transactional email** through SendGrid
- **REST API + Swagger UI** with JWT bearer auth documented
- **EF Core migrations**, seed data, and a clean layering: Controllers → Services →
  Validators → Models/ViewModels

## Tech stack

| Area | Tech |
|------|------|
| Framework | ASP.NET Core MVC, .NET 8 |
| Real-time | SignalR |
| Data | Entity Framework Core — SQLite (dev), PostgreSQL (prod) |
| Auth | ASP.NET Identity, JWT bearer for API |
| Email | SendGrid |
| API docs | Swashbuckle / Swagger |
| Deployment | Docker (multi-stage build) |

## Run locally

```bash
git clone https://github.com/naibwedi/Logi-Track.git
cd Logi-Track
dotnet restore
dotnet ef database update      # applies migrations to the SQLite dev DB
dotnet run
```

App: `https://localhost:5001` · Swagger: `https://localhost:5001/swagger`

Configuration (`appsettings.json` or user-secrets):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=app.db",
    "PostgreSQLConnection": "Host=...;Database=...;Username=...;Password=..."
  },
  "SendGrid": { "ApiKey": "your-key" }
}
```

### Docker

```bash
docker build -t logitrack .
docker run -p 8080:8080 logitrack
```

## Architecture notes

- The environment decides the database: SQLite when `Development`, PostgreSQL otherwise.
- Roles (`SuperAdmin`, `Admin`, `Customer`, `Driver`) and the initial SuperAdmin are
  created at boot in `Program.cs` before the app starts serving requests.
- `TripHub` groups clients by `trip_{id}` so status/price/driver events only reach
  the users involved in that trip; admins join an `Admins` group for dashboard stats.
- Cross-cutting rules live in `Services/` (e.g. `ApprovedOnlyAttribute` gates
  unapproved drivers) and `Validators/` (e.g. `AgeValidator`).

## Status

University project, 2024 (142 commits). Demonstrates real-time full-stack .NET,
EF migrations, Identity, and Docker.
