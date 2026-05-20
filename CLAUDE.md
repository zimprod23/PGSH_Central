# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Is

**PGSH** (Postgraduation Schedule Helper) — a medical internship and academic scheduling management system. It manages hospitals, centers, services, student registrations, rotation stages, cohorts, and attendance for medical postgrad programs.

## Build & Run Commands

```bash
# Build the entire solution
dotnet build PGSH.sln

# Run the full stack (API + PostgreSQL + Keycloak + Redis + frontend via Aspire)
dotnet run --project PGSH.AppHost

# Frontend only
cd PGSH.Frontend
npm install
npm run dev       # Vite dev server, port 5173
npm run build
npm run lint

# Add a new EF Core migration (run from repo root)
dotnet ef migrations add MigrationName --project PGSH.Infrastructure --startup-project PGSH.API

# Apply migrations manually (MigrationService also runs them on Aspire startup)
dotnet ef database update --project PGSH.Infrastructure --startup-project PGSH.API
```

## Solution Structure

```
PGSH.sln
├── PGSH.AppHost/          # .NET Aspire orchestration (PostgreSQL, Keycloak, Redis, API, frontend)
├── PGSH.MigrationService/ # EF Core migration worker; runs migrations at startup
├── PGSH.ServiceDefaults/  # Shared Aspire config: telemetry, resilience, health checks
├── PGSH.API/              # ASP.NET Core 9 minimal API (Endpoints/, Extensions/, Middleware/)
├── PGSH.Application/      # CQRS commands & queries via MediatR
├── PGSH.Domain/           # Domain entities, value objects, enums
├── PGSH.Infrastructure/   # EF Core DbContext, Keycloak auth, authorization, migrations
├── PGSH.SharedKernel/     # Base types: Entity, Result<T>, Error, DomainEvent
└── PGSH.Frontend/         # React 19 + TypeScript + Vite + Mantine UI + Redux + Keycloak
```

## Architecture

**Clean Architecture** — Domain → SharedKernel ← Application ← Infrastructure ← API.

### CQRS / MediatR
All business logic lives in `PGSH.Application/` as commands (`*Command`) and queries (`*Query`). API endpoints send them through MediatR `ISender`. Pipeline behaviors handle request logging and FluentValidation.

### Minimal Endpoints
Every endpoint implements `IEndpoint` (defined in `PGSH.SharedKernel`). `EndpointExtensions` auto-discovers all `IEndpoint` implementations via reflection and registers them. To add a new endpoint, create a class implementing `IEndpoint` in `PGSH.API/Endpoints/<domain>/`.

### Result Pattern
All handlers return `Result<T>` (from `PGSH.SharedKernel`). Endpoints map failures to HTTP problem responses via `CustomResults.Problem(result)`. Never throw exceptions for expected business failures.

### Domain Events
Entities (inheriting `Entity` from `PGSH.SharedKernel`) raise domain events by calling `RaiseDomainEvent(...)`. The `ApplicationDbContext.SaveChangesAsync` override publishes them after the transaction commits.

### Database
- **PostgreSQL** via EF Core 9 + Npgsql, with `EFCore.NamingConventions` for snake_case column names.
- `ApplicationDbContext` is in `PGSH.Infrastructure/Database/`. Entity configurations are in `PGSH.Infrastructure/Database/Configurations/`.
- When Aspire runs, the connection string is injected automatically. In standalone dev, it reads from `appsettings.Development.json`.

### Authentication & Authorization
- **Keycloak** (realm `pgsh`, port 8082) issues JWT tokens validated by `Aspire.Keycloak.Authentication`.
- `KeycloakRoleTransformer` maps Keycloak roles to internal permissions.
- `HasPermission` attribute on endpoints triggers `PermissionAuthorizationHandler`.
- `UserContext` exposes the current user's ID from claims.

### API Documentation
Scalar UI is served at `/scalar/v1`. Keycloak OAuth2 is wired into the UI for authenticated requests.

## Key Design Conventions

- **NuGet versions** are centralized in `Directory.Packages.props` — don't add `Version=` in `.csproj` files.
- **Implicit usings** are enabled; no need for `using System;` etc.
- **Nullable reference types** are enabled project-wide.
- Domain entities go in `PGSH.Domain/`, value objects and enums alongside them.
- Application layer handlers use FluentValidation for input validation; validators are registered automatically.
- CORS is open (`AllowAllForDev`) in development; lock it down before production.
