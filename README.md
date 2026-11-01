# Logi-Track

ASP.NET Core MVC logistics application for managing customer trip requests,
admin pricing and approval, driver assignments, completion, and payment records.

## Current features

- Customer registration, approval, trip requests, price response, and trip history
- Admin customer approval, driver management, pricing, assignment, trip history,
  completed-trip invoicing report, and payment overview
- Driver current trips, completion workflow, history, and payment tracking
- SuperAdmin administration of admin accounts
- Authenticated SignalR subscriptions with server-side trip access checks
- ASP.NET Core Identity roles and login lockout
- SQLite development database and PostgreSQL production database
- EF Core migrations, Docker packaging, integration tests, and GitHub Actions CI

This repository is an MVC web application. It does not currently expose a public
REST/JWT API.

## Local development

Requirements: .NET 8 SDK.

```bash
git clone https://github.com/naibwedi/Logi-Track.git
cd Logi-Track
dotnet restore
dotnet run
```

The application applies EF Core migrations and creates the four Identity roles at
startup. It intentionally does **not** create fixed-password users. Register customers
through the UI. For the first startup only, supply `BootstrapAdmin__Email` and
`BootstrapAdmin__Password`; remove both variables immediately after that account exists.

Run verification with:

```bash
dotnet build
dotnet test
```

## Configuration

Never commit real credentials. For development, use .NET user secrets:

```bash
dotnet user-secrets set "SendGrid:ApiKey" "..."
dotnet user-secrets set "SendGrid:FromEmail" "verified-sender@example.com"
```

Production configuration is supplied through environment variables:

- `ConnectionStrings__PostgreSQLConnection`
- `SendGrid__ApiKey`
- `SendGrid__FromEmail`
- `SendGrid__FromName`
- `Contact__Recipient`
- `DataProtection__KeysPath` (use a persistent mounted directory)
- `BootstrapAdmin__Email` and `BootstrapAdmin__Password` (first startup only)

Production startup fails clearly when PostgreSQL, SendGrid, or the contact recipient is missing.

## Docker

```bash
docker build -t logitrack .
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__PostgreSQLConnection="..." \
  -e SendGrid__ApiKey="..." \
  -e SendGrid__FromEmail="..." \
  -e Contact__Recipient="..." \
  -e DataProtection__KeysPath="/app/.keys" \
  -v logitrack-keys:/app/.keys \
  logitrack
```

## Security notice

Older revisions contained credentials and fixed seed passwords. Removing values from
the current branch does not invalidate leaked credentials or erase Git history.
Rotate all affected database and SendGrid credentials before deployment, then use a
history-rewrite tool if the repository must no longer retain the old values.
