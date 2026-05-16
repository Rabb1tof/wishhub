# Changelog

All notable changes to **WishHub** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.1] — 2026-05-16

### Fixed

- Fix EasyPanel registry deploy when `IMAGE_OWNER` is not provided by using
  `rabb1tof` as the default GHCR namespace in `docker-compose.registry.yml`.
- Fix production sign-up/sign-in token generation by explicitly passing
  `Jwt__AccessTokenExpiryMinutes` and `Jwt__RefreshTokenExpiryDays` through
  deploy compose files.
- Improve JWT configuration validation so missing or invalid token lifetime
  settings fail with a clear configuration error instead of
  `ArgumentNullException`.

### Changed

- Document JWT token lifetime environment variables in `.env.example`.

## [0.1.0] — 2026-04-27

> 🐣 **First public test release.** This is an early build intended for closed
> testing — expect rough edges, breaking changes between minor versions, and
> occasional parser hiccups when marketplaces change their HTML.

### Added

#### Core wishlist
- Add products by pasting a URL from **Ozon**, **Wildberries** or **Yandex Market** —
  name, image and current price are parsed automatically.
- Personal wishlist with custom item names, notes, filters and sorting.
- Image proxy with caching so external CDN URLs don't leak referrer info.

#### Social
- Friend requests, follows, and a public profile page (`/profile/:username`).
- Browse a friend's wishlist; reserve items so the owner doesn't see them
  marked as taken.
- Direct messages between friends in real time over **SignalR**.

#### Auth
- Email + password sign-up / sign-in via **ASP.NET Core Identity** + JWT.
- **VK OAuth2** sign-in.
- **Telegram Login Widget** sign-in.
- Multiple providers can be linked to the same account.

#### Background tasks
- **Hangfire** job updates prices on a per-user-configurable interval (default 8h).
- Stale-price detection so users see "last updated 3h ago" on each item.

#### Infrastructure
- Containerised stack: **PostgreSQL 16**, **Redis 7**, .NET 9 API, Vite/nginx frontend.
- Two compose flavours:
  - `docker-compose.dev.yml` — local development with hot reload.
  - `docker-compose.registry.yml` — production deploy that pulls prebuilt images
    from **GHCR** (`ghcr.io/rabb1tof/wishhub-{api,frontend}`).
- GitHub Actions workflow `build-and-push.yml` builds and tags both images on
  every push to `main`.
- Container `HEALTHCHECK` and `/health` endpoint for orchestrators
  (Docker, EasyPanel, etc.).

### Known limitations / caveats
- No password reset flow yet — if you lose your password, you currently need
  to re-register with another email.
- No 2FA.
- Marketplace parsers rely on Playwright + stealth tricks; aggressive bot
  protection on Ozon may occasionally fail a request and require a retry.
- Hangfire dashboard at `/hangfire` is **not** exposed through the public
  reverse proxy — administrators access it via direct container connection.
- API image is ~1.4 GB (Chromium-based scraping). Cannot be reduced further
  without rewriting parsers to use `chromium-headless-shell`.

### Deployment notes
- Repo is **public**; secrets live exclusively in `.env` / EasyPanel UI and are
  never committed.
- GHCR packages (`wishhub-api`, `wishhub-frontend`) are public — anyone can
  `docker pull` them without authentication.
- Migrations run automatically at API startup
  (`db.Database.Migrate()` in `Program.cs`).

### Internal
- New project name normalisation: everything is now under the **WishHub.\***
  namespace (was previously a mix of `WishList.*` and `WishHub.*` in some docs).
- Documentation reshuffled — see `README.md` for the current map.

[unreleased]: https://github.com/Rabb1tof/wishhub/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/Rabb1tof/wishhub/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Rabb1tof/wishhub/releases/tag/v0.1.0
