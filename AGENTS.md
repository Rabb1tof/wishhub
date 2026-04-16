# AGENTS.md

This file is the primary instruction set for any AI agent (Codex, Claude, etc.) working on this repository.
Read it fully before touching any file. Re-read the relevant sections before each task.

---

## Table of Contents

1. [Project Overview](#project-overview)
2. [Repository Layout](#repository-layout)
3. [The Plan — Your Source of Truth](#the-plan--your-source-of-truth)
4. [Getting Started Locally](#getting-started-locally)
5. [How to Work on a Task](#how-to-work-on-a-task)
6. [Backend Conventions (.NET 9)](#backend-conventions-net-9)
7. [Frontend Conventions (React + Vite)](#frontend-conventions-react--vite)
8. [Database & Migrations](#database--migrations)
9. [Testing Requirements](#testing-requirements)
10. [Git Conventions](#git-conventions)
11. [What You Must Never Do](#what-you-must-never-do)

---

## Project Overview

**Wishlist** is a full-stack web application where users can:
- Add products from Ozon, Wildberries, and Yandex Market by pasting a URL — the app parses price, name, and image automatically
- Manage a personal wishlist with filters, sorting, and custom item names
- Follow other users, send friend requests, and browse friends' wishlists
- Reserve items from a friend's wishlist (hidden from the wishlist owner)
- Send direct messages in real time via WebSocket (SignalR)
- Sign in with login/password, VK OAuth2, or Telegram Login Widget; accounts can be linked

**Stack summary:**

| Layer | Technology |
|---|---|
| API | .NET 9 — ASP.NET Core Web API |
| ORM | Entity Framework Core 9 |
| Database | PostgreSQL 16 |
| Cache | Redis 7 |
| Scraping | Microsoft.Playwright (headless Chromium) + HtmlAgilityPack |
| Background jobs | Hangfire + Hangfire.PostgreSql |
| Real-time | ASP.NET Core SignalR |
| Auth | ASP.NET Core Identity + JWT Bearer + VK OAuth2 + Telegram Widget |
| Frontend | React 18, Vite, TypeScript |
| HTTP client (FE) | Axios + TanStack Query v5 |
| State (FE) | Zustand |
| Router (FE) | React Router v6 |
| Containers | Docker + Docker Compose |

---

## Repository Layout

```
/
├── AGENTS.md                  ← you are here
├── plan.md                    ← task checklist — your source of truth
├── docker-compose.yml         ← local dev infrastructure (postgres, redis)
├── docker-compose.prod.yml    ← production stack
├── WishList.sln
│
├── WishList.Api/              ← ASP.NET Core entry point
│   ├── Controllers/
│   ├── Hubs/                  ← SignalR hubs
│   ├── Middleware/
│   ├── Extensions/            ← IServiceCollection extension methods
│   ├── Program.cs
│   └── appsettings.json
│
├── WishList.Core/             ← Domain layer — no framework dependencies
│   ├── Entities/
│   ├── Interfaces/
│   ├── DTOs/
│   └── Exceptions/
│
├── WishList.Infrastructure/   ← EF Core, repositories, background jobs
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   ├── Configurations/
│   │   └── Migrations/
│   ├── Repositories/
│   ├── Services/
│   └── BackgroundJobs/
│
├── WishList.Parsing/          ← Product scrapers, isolated from the rest
│   ├── Parsers/
│   ├── ParserFactory.cs
│   └── Models/
│
├── WishList.Tests/            ← xUnit tests
│   ├── Parsing/
│   ├── Api/
│   └── Infrastructure/
│
└── frontend/                  ← React + Vite app
    └── src/
        ├── api/
        ├── components/
        ├── hooks/
        ├── pages/
        ├── store/
        └── types/
```

**Dependency rules (enforced):**
- `WishList.Core` has zero references to EF Core, ASP.NET, or any infrastructure library
- `WishList.Parsing` references only `WishList.Core` and parsing libraries
- `WishList.Api` never contains business logic — only wires things together
- Circular project references are forbidden

---

## The Plan — Your Source of Truth

> **Before starting any task, open `plan.md` and read it.
> After completing any task, open `plan.md` and update it.
> This is not optional.**

`plan.md` is a structured checklist with 14 stages. Each item looks like this:

```markdown
- [ ] Task that has not been started
- [x] Task that has been completed
```

### Rules for updating plan.md

1. **Mark an item `[x]` only when it is fully implemented, committed, and the relevant tests pass.**
   Do not mark it done speculatively or halfway through.

2. **Never delete items.** If a task turns out to be unnecessary, add a note next to it:
   ```markdown
   - [x] Some task — skipped, not needed because X
   ```

3. **If you discover a subtask that is missing from the plan**, add it under the relevant stage with a `[ ]` before you start working on it. Do not add tasks retroactively to make the plan look complete.

4. **If a task is blocked** (waiting on a secret, external API key, or decision), mark it:
   ```markdown
   - [ ] Register VK application — BLOCKED: needs Client ID from project owner
   ```

5. **At the start of each working session**, scan all `[ ]` items and pick the next unchecked item in the lowest-numbered stage. Work in order unless there is an explicit dependency reason not to.

6. **Never work on two stages simultaneously.** Finish stage N before touching stage N+1.

---

## Getting Started Locally

### Prerequisites

- .NET 9 SDK
- Node.js 20+
- Docker + Docker Compose
- `dotnet-ef` tool: `dotnet tool install --global dotnet-ef`

### First-time setup

```bash
# 1. Start infrastructure
docker-compose up -d

# 2. Restore backend
dotnet restore

# 3. Apply migrations
dotnet ef database update \
  --project WishList.Infrastructure \
  --startup-project WishList.Api

# 4. Run backend
dotnet run --project WishList.Api

# 5. Install frontend dependencies
cd frontend && npm install

# 6. Run frontend
npm run dev
```

Backend runs on `http://localhost:5000`.
Frontend runs on `http://localhost:5173` and proxies `/api` and `/hubs` to the backend.

### Environment variables

Secrets go in `WishList.Api/appsettings.Development.json` (git-ignored).
Never hardcode secrets. Never commit secrets.
Required keys are listed in `appsettings.json` with placeholder values like `"REPLACE_ME"`.

---

## How to Work on a Task

Follow this sequence for every task:

```
1. Read plan.md — find the next unchecked item
2. Understand the task — re-read the relevant section of AGENTS.md if needed
3. Write the code
4. Write or update tests for the changed code
5. Run the relevant tests — all must pass
6. Run the full test suite — must not regress
7. Update plan.md — mark the item [x]
8. Commit with a conventional commit message (see Git Conventions)
```

If a task requires a database migration, always run it and verify the generated SQL looks correct before committing.

If a task requires installing a new NuGet or npm package, add it to the correct project file. Do not install packages globally.

---

## Backend Conventions (.NET 9)

### General

- Use `async`/`await` throughout. Never use `.Result` or `.Wait()`.
- All `DateTime` values must be `DateTime.UtcNow` — never `DateTime.Now`.
- PostgreSQL stores timestamps as `timestamp with time zone`. Configure Npgsql accordingly:
  ```csharp
  AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", false);
  ```
- Use `Guid.NewGuid()` for all primary keys. Never use `int` identity keys.
- Use `CancellationToken` in all async methods that touch I/O.

### Controllers

- Controllers are thin. They validate input, call a service, and return a DTO.
- Business logic belongs in `WishList.Infrastructure/Services/` or `WishList.Core/`.
- Return types: use `ActionResult<T>` and appropriate HTTP status codes.
  - `200 OK` — successful GET or PATCH
  - `201 Created` with `Location` header — successful POST that creates a resource
  - `204 No Content` — successful DELETE
  - `400 Bad Request` — validation failure (handled automatically by `[ApiController]`)
  - `401 Unauthorized` — missing or invalid token
  - `403 Forbidden` — authenticated but not allowed
  - `404 Not Found` — resource does not exist
  - `409 Conflict` — duplicate resource or state conflict (e.g. already reserved)

### DTOs

- Requests: suffix `Request` (e.g. `AddWishlistItemRequest`)
- Responses: suffix `Dto` (e.g. `WishlistItemDto`)
- Keep request and response DTOs in separate files under `WishList.Core/DTOs/`
- Never expose EF entities directly from controllers

### Error handling

- All unhandled exceptions are caught by `ExceptionHandlingMiddleware` and returned as `ProblemDetails`
- Throw domain exceptions from `WishList.Core/Exceptions/` for expected error states
- Do not swallow exceptions silently — log at `Error` level and rethrow or convert

### Dependency injection

- Register services in `WishList.Api/Extensions/ServiceCollectionExtensions.cs` using extension methods grouped by feature (e.g. `AddParsing()`, `AddAuth()`, `AddHangfire()`)
- Use `AddScoped` for services that touch DbContext, `AddSingleton` for stateless services (e.g. `ParserFactory`, `IPlaywrightBrowserPool`)

### Validation

- Use Data Annotations on DTOs for simple rules (`[Required]`, `[MaxLength]`, `[Url]`)
- If a DTO needs complex rules, use FluentValidation with a dedicated `Validator<T>` class
- `[ApiController]` attribute handles returning `400` for model validation failures automatically

---

## Frontend Conventions (React + Vite)

### General

- Strict TypeScript. No `any`. No `// @ts-ignore`.
- All API responses must have corresponding types in `src/types/`.
- Use TanStack Query for all server state. Do not store server data in Zustand.
- Use Zustand only for client state: auth tokens, UI preferences, SignalR connection.
- Components are function components only. No class components.
- One component per file. File name matches component name (PascalCase).

### API client (`src/api/client.ts`)

- Single Axios instance with base URL `"/api"`
- Request interceptor: attach `Authorization: Bearer {token}` from Zustand store
- Response interceptor: on `401`, attempt token refresh once; if refresh fails, clear auth and redirect to `/login`
- All API calls go through typed functions in `src/api/` — never call Axios directly from components

### Images

- Never use a product image URL directly from the API response.
- Always construct the proxy URL:
  ```ts
  const proxyUrl = (imageUrl: string) =>
    `/api/images/proxy?url=${encodeURIComponent(imageUrl)}`;
  ```
- The `ProductDto` from the API already includes `imageProxyUrl` — use that field directly.

### Routing

- Protected routes use `<PrivateRoute>` which checks Zustand auth state and redirects to `/login` if unauthenticated
- Public routes: `/login`, `/register`, `/profile/:username`
- Private routes: `/`, `/wishlist`, `/messages`, `/friends`, `/settings`

### Forms

- Use controlled components with local `useState` for form state
- Show inline validation errors
- Disable submit button while a mutation is in flight (`mutation.isPending`)
- Show a toast notification on success and on error

---

## Database & Migrations

- All schema changes go through EF Core migrations. Never alter the database manually.
- Migration naming convention: `PascalCase` description of the change, e.g. `AddWishlistPrivacyField`
- After generating a migration, review the generated `Up()` and `Down()` methods before committing.
- Every migration must have a working `Down()` method.
- Migrations run automatically on app start in all environments (`db.Database.MigrateAsync()`).

```bash
# Add a migration
dotnet ef migrations add <MigrationName> \
  --project WishList.Infrastructure \
  --startup-project WishList.Api

# Remove last migration (only if not applied)
dotnet ef migrations remove \
  --project WishList.Infrastructure \
  --startup-project WishList.Api

# Apply migrations
dotnet ef database update \
  --project WishList.Infrastructure \
  --startup-project WishList.Api
```

---

## Testing Requirements

- Every new service method must have at least one unit test.
- Every new controller endpoint must have at least one integration test.
- Tests live in `WishList.Tests/` mirroring the source project structure.
- Integration tests use `WebApplicationFactory<Program>` + `Testcontainers.PostgreSql` (a real DB spun up in Docker).
- Parser tests that hit real URLs are tagged `[Trait("Category", "Integration")]` and are excluded from the default test run.
- Test naming: `MethodName_StateUnderTest_ExpectedBehavior`
  - Example: `ParseAsync_ValidWildberriesUrl_ReturnsProductWithPrice`

```bash
# Run unit tests only
dotnet test --filter "Category!=Integration"

# Run all tests including integration
dotnet test

# Run frontend type check
cd frontend && npx tsc --noEmit

# Run frontend tests (if any)
cd frontend && npm test
```

**A task is not complete until tests pass.** If you cannot write a test for something, leave a `// TODO: test` comment and add a corresponding `[ ]` item to `plan.md`.

---

## Git Conventions

### Branch naming

```
feature/<short-description>     # new functionality
fix/<short-description>         # bug fix
chore/<short-description>       # tooling, config, deps
refactor/<short-description>    # code changes with no behaviour change
```

### Commit messages — Conventional Commits

Format: `<type>(<scope>): <short description>`

| Type | When to use |
|---|---|
| `feat` | A new feature |
| `fix` | A bug fix |
| `chore` | Build process, dependency updates, tooling |
| `refactor` | Code change that neither fixes a bug nor adds a feature |
| `test` | Adding or fixing tests |
| `docs` | Documentation only |

**Scope** is the project or area: `api`, `parsing`, `frontend`, `db`, `auth`, `wishlist`, `messages`, `friends`

Examples:
```
feat(parsing): add WildberriesParser with open card API
feat(api): add POST /wishlist endpoint
fix(auth): return 401 on expired refresh token instead of 500
chore(db): add migration AddWishlistPrivacyField
test(parsing): add unit tests for OzonParser HTML fallback
```

- Commits should be atomic — one logical change per commit.
- Do not commit commented-out code.
- Do not commit `appsettings.Development.json` or any file containing secrets.

---

## What You Must Never Do

- **Never expose `ReservedById` or `IsReserved=true` to the wishlist owner.** The owner must not know who reserved their items or that items are reserved. Strip these fields in the DTO mapping when the requester is the owner.
- **Never store a raw product image URL in the frontend.** Always route through `/api/images/proxy`.
- **Never use `DateTime.Now`.** Use `DateTime.UtcNow` everywhere.
- **Never use `.Result` or `.Wait()` on a Task.** This causes deadlocks.
- **Never query the database inside a loop.** Use `Include`, batch queries, or `Contains` with a list.
- **Never return EF entities from a controller.** Always map to a DTO first.
- **Never hardcode secrets** (JWT secret, OAuth client IDs, bot tokens). All secrets come from configuration.
- **Never mark a plan.md item `[x]` without running the tests first.**
- **Never skip updating plan.md** after completing a task.
- **Never commit directly to `main`.** All changes go through a branch.