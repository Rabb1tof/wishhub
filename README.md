# WishHub

> Сервис для удобной работы с вишлистами: добавляй товары с **Ozon**,
> **Wildberries** и **Яндекс.Маркета** одной ссылкой, отслеживай цены,
> делись со своими друзьями.

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Version](https://img.shields.io/badge/version-0.1.0-blue.svg)](CHANGELOG.md)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![React 18](https://img.shields.io/badge/React-18-61DAFB)](https://react.dev/)

---

## Что умеет

- 🔗 **Парсинг по ссылке** — вставь URL товара с Ozon / Wildberries / Яндекс.Маркета,
  название, картинка и цена подтянутся сами.
- 📋 **Личный вишлист** — кастомные имена, заметки, фильтры, сортировка.
- 👥 **Друзья и подписки** — публичный профиль `/profile/:username`,
  заявки в друзья, лента активности.
- 🎁 **Резервирование подарков** — друг может «занять» товар у тебя из вишлиста,
  ты не увидишь кто и что (сюрприз сохраняется).
- 💬 **Сообщения в реальном времени** — приватные чаты на **SignalR**.
- 🔐 **Гибкий вход** — email/пароль, **VK OAuth2** или **Telegram Login Widget**;
  несколько провайдеров можно связать с одним аккаунтом.
- ⏱ **Автообновление цен** — Hangfire-джоба перепарсивает товары по индивидуальному
  графику каждого пользователя (по умолчанию раз в 8 часов).

---

## Стек

| Слой | Технология |
|---|---|
| API | .NET 9, ASP.NET Core Web API |
| ORM | Entity Framework Core 9 |
| База | PostgreSQL 16 |
| Кэш / pub-sub | Redis 7 |
| Парсинг | Microsoft Playwright (Chromium) + HtmlAgilityPack |
| Фоновые задачи | Hangfire + Hangfire.PostgreSql |
| Real-time | ASP.NET Core SignalR |
| Auth | ASP.NET Core Identity + JWT Bearer + VK OAuth2 + Telegram Widget |
| Frontend | React 18, Vite, TypeScript |
| HTTP-клиент (FE) | Axios + TanStack Query v5 |
| State (FE) | Zustand |
| Routing (FE) | React Router v6 |
| Контейнеризация | Docker + Docker Compose |
| Реестр образов | GitHub Container Registry (`ghcr.io`) |
| CI | GitHub Actions |

---

## Архитектура (упрощённо)

```
                     ┌────────────────────┐
                     │  Reverse proxy /   │  (EasyPanel Traefik /
                     │  Let's Encrypt SSL │   nginx + certbot)
                     └─────────┬──────────┘
                               │ https
                  ┌────────────▼─────────────┐
                  │  frontend (nginx)        │   :80
                  │  - serves /dist          │
                  │  - proxies /api, /hubs,  │
                  │    /avatars              │
                  └────────┬─────────────────┘
                           │ http
              ┌────────────▼──────────────┐
              │  api (.NET 9)              │   :5000
              │  - controllers, signalR    │
              │  - migrations on startup   │
              │  - hangfire jobs           │
              │  - playwright/chromium     │
              └───┬─────────────────────┬──┘
                  │                     │
        ┌─────────▼────────┐    ┌───────▼──────┐
        │ postgres:16      │    │ redis:7      │
        │ - app data       │    │ - cache      │
        │ - hangfire jobs  │    │ - signalR bus│
        └──────────────────┘    └──────────────┘
```

---

## Быстрый старт

Требования: **Docker Desktop** и `git`.

### Вариант 1. Запустить production-образы из GHCR (рекомендую для smoke-теста)

```bash
git clone https://github.com/Rabb1tof/wishhub.git
cd wishhub

# 1. Скопируй шаблон env и заполни секреты
cp .env.example .env
# открой .env, как минимум проставь:
#   IMAGE_OWNER=rabb1tof
#   IMAGE_TAG=latest
#   POSTGRES_PASSWORD=<любой непустой>
#   JWT_SECRET=<openssl rand -base64 64>
#   PUBLIC_URL=http://localhost:5173

# 2. Запусти production-образы локально (с локальным override-файлом, который
#    публикует порты на host)
docker compose -f docker-compose.registry.yml -f docker-compose.local.yml up -d

# 3. Открывай
open http://localhost:5173
```

API будет доступен на `http://localhost:5050`, frontend — на `http://localhost:5173`.

> Apple Silicon: образы собраны под `linux/amd64`, Docker Desktop запустит их через
> Rosetta/QEMU. Первый старт API ~1–2 минуты (мы расширили `start_period` healthcheck'а
> для эмуляции). Это нормально.

### Вариант 2. Локальная разработка с hot-reload (для написания кода)

```bash
# Поднять только инфру (postgres + redis)
docker compose up -d

# Backend
dotnet restore
dotnet run --project WishHub.Api

# Frontend (в другом терминале)
cd frontend
npm install
npm run dev
```

Frontend на `http://localhost:5173`, API на `http://localhost:5000`,
Vite сам проксирует `/api` и `/hubs` к API.

Подробности — в [`WishHub.Api/README.md`](WishHub.Api/README.md) и
[`frontend/README.md`](frontend/README.md).

### Остановить и почистить

```bash
docker compose -f docker-compose.registry.yml -f docker-compose.local.yml down
# Если хочется снести данные тоже (postgres, avatars, redis):
docker compose -f docker-compose.registry.yml -f docker-compose.local.yml down -v
```

---

## Документация

| Документ | О чём |
|---|---|
| [`AGENTS.md`](AGENTS.md) | Главный набор правил для AI-агентов и контрибьюторов: структура репозитория, конвенции кода, ветки и коммиты. **Читай первым** перед любым PR. |
| [`plan.md`](plan.md) | Чек-лист этапов разработки (14 этапов). Источник истины: что сделано, что в работе. |
| [`CHANGELOG.md`](CHANGELOG.md) | История релизов в формате Keep a Changelog. |
| [`CONFIGURATION.md`](CONFIGURATION.md) | Структура конфигов: где какой `.env`, какой `appsettings.*.json`, что в `.gitignore`. |
| [`DEPLOY-OPTIMIZED.md`](DEPLOY-OPTIMIZED.md) | **Рекомендуемый production-деплой**: GHCR + EasyPanel, готовые образы, без сборки на сервере. |
| [`EASYPANEL.md`](EASYPANEL.md) | Старая схема EasyPanel (со сборкой прямо на сервере). Оставлена как fallback. |
| [`DEPLOY.md`](DEPLOY.md) | Ручной деплой на bare VPS со своим nginx + Let's Encrypt + certbot. |
| [`WishHub.Api/README.md`](WishHub.Api/README.md) | Гайд по бэкенду: миграции, JWT, OAuth-настройка, swagger. |
| [`frontend/README.md`](frontend/README.md) | Гайд по фронту: dev-сервер, Vite-прокси, Telegram widget на localhost. |
| [`LICENSE`](LICENSE) | MIT. |

---

## Деплой

Готовые образы публикуются в GHCR на каждый push в `main`:

- API: `ghcr.io/rabb1tof/wishhub-api:latest`
- Frontend: `ghcr.io/rabb1tof/wishhub-frontend:latest`

EasyPanel подтягивает их по `docker-compose.registry.yml`, ничего не собирая
на сервере. Сервер чистый, образ обновляется одной кнопкой Redeploy.

Пошаговая инструкция — в [`DEPLOY-OPTIMIZED.md`](DEPLOY-OPTIMIZED.md).

---

## Структура репозитория

```
wishhub/
├── README.md                       ← главный документ
├── AGENTS.md                       ← правила для AI-агентов
├── plan.md                         ← чек-лист разработки
├── CHANGELOG.md                    ← история релизов
├── LICENSE                         ← MIT
│
├── DEPLOY-OPTIMIZED.md             ← деплой через GHCR + EasyPanel
├── EASYPANEL.md                    ← деплой через сборку на сервере (legacy)
├── DEPLOY.md                       ← ручной деплой на VPS
├── CONFIGURATION.md                ← карта конфигов и секретов
│
├── docker-compose.yml              ← dev: postgres + redis для локальной разработки
├── docker-compose.dev.yml          ← dev: + сборка api/frontend для тестирования контейнеров
├── docker-compose.prod.yml         ← prod: ручная сборка (legacy)
├── docker-compose.registry.yml     ← prod: pull готовых образов из GHCR (рекомендуется)
├── docker-compose.local.yml        ← override для локального теста registry-образов
│
├── WishHub.sln
│
├── WishHub.Api/                    ← ASP.NET Core Web API (точка входа)
│   ├── Controllers/
│   ├── Hubs/                       ← SignalR
│   ├── Middleware/
│   ├── Extensions/                 ← DI helpers
│   ├── Dockerfile
│   ├── Program.cs
│   └── appsettings.json
│
├── WishHub.Core/                   ← Domain layer (без зависимостей от инфры)
│   ├── Entities/
│   ├── Interfaces/
│   ├── DTOs/
│   └── Exceptions/
│
├── WishHub.Infrastructure/         ← EF Core, репозитории, фоновые задачи
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   ├── Configurations/
│   │   └── Migrations/
│   ├── Repositories/
│   ├── Services/
│   └── BackgroundJobs/
│
├── WishHub.Parsing/                ← Парсеры товаров (изолированный модуль)
│   ├── Parsers/
│   ├── Playwright/
│   ├── Ozon/
│   └── Models/
│
├── WishHub.Tests/                  ← xUnit
│
├── frontend/
│   ├── Dockerfile
│   ├── nginx.conf                  ← reverse-proxy конфиг для прода
│   └── src/
│       ├── api/
│       ├── components/
│       ├── hooks/
│       ├── pages/
│       ├── store/
│       └── types/
│
├── scripts/
│   ├── build-and-push.sh           ← ручной push в GHCR
│   ├── dev-tunnel.sh               ← VS Code dev tunnel helper
│   └── sync-env.sh
│
└── .github/workflows/
    └── build-and-push.yml          ← CI: сборка + push в GHCR
```

---

## Лицензия

[MIT](LICENSE) © 2026 Artyom Panin
