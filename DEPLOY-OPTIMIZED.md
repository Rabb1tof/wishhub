# WishHub: Оптимизированный деплой через GHCR + EasyPanel

> Этот документ — основной гайд по деплою.
> `DEPLOY.md` (ручной docker-compose с собственным nginx + Let's Encrypt) и
> `EASYPANEL.md` (старая схема со сборкой на сервере) остаются как fallback.

## Зачем это нужно

Раньше EasyPanel клонировал репу и собирал образы прямо на сервере. Это:

- Жрало **5–7 ГБ временно** под BuildKit cache
- Финальный API-образ был **~1.2 ГБ** (внутри лежал PowerShell tarball + дублирующиеся apt-deps)
- Каждый редеплой = заново качаются NuGet и `node_modules`

Новая схема:

1. **GitHub Actions** собирают образы и пушат в **GHCR** (`ghcr.io/<user>/wishhub-api`, `wishhub-frontend`)
2. **EasyPanel** подтягивает уже готовые образы через `docker-compose.registry.yml`
3. Сервер вообще ничего не собирает — только `docker pull` и старт контейнеров

## Что поменялось в файлах

| Файл | Что |
|---|---|
| `WishHub.Api/Dockerfile` | NuGet кэш-слой, без pwsh в runtime, ручные apt-deps Chromium → API ~600 МБ вместо ~1.2 ГБ |
| `frontend/Dockerfile` | `node:20-slim` вместо `node:20` (build-image втрое меньше), npm cache mount |
| `frontend/nginx.conf` | Добавлены `/avatars/`, кэш статики, websocket-таймауты — frontend теперь самостоятельный reverse proxy |
| `.dockerignore` | Жёстче — `.windsurf`, `.github`, `**/.git`, `WishHub.Tests/`, `*.md` и т.д. |
| `docker-compose.registry.yml` | Новый compose: `image:` вместо `build:`, без отдельного nginx-сервиса |
| `.github/workflows/build-and-push.yml` | CI: build + push в GHCR на каждый `push main` и `v*.*.*` тег |
| `scripts/build-and-push.sh` | Ручной push с локальной машины (если не хочется ждать CI) |
| `.env.example` | Добавлены `IMAGE_OWNER`, `IMAGE_TAG` |

## Шаг 1. Проверить локально

Перед первым CI-билдом убедись что Dockerfile собирается на твоей машине:

```bash
# API
docker build -f WishHub.Api/Dockerfile -t wishhub-api:test .
docker images wishhub-api:test --format "{{.Size}}"

# Frontend
docker build -f frontend/Dockerfile -t wishhub-frontend:test ./frontend
docker images wishhub-frontend:test --format "{{.Size}}"
```

Ожидаемые размеры:
- `wishhub-api:test` ≈ **1.35–1.40 ГБ** (раньше было ~1.81 ГБ; основная масса — это
  Chromium-браузер ~542 МБ + apt-deps ~250 МБ + base aspnet ~220 МБ + app ~140 МБ).
  Сильнее ужать без переписывания парсеров на `chromium-headless-shell` не получится —
  сам браузер столько весит.
- `wishhub-frontend:test` ≈ **90–100 МБ** (nginx:alpine + статический dist).

Если API сильно больше 1.5 ГБ — проверь, что в `WishHub.Api/Dockerfile` есть строка
`rm -rf /ms-playwright/chromium_headless_shell-*` после `playwright install chromium` —
без неё лишние ~300 МБ останутся.

## Шаг 2. Сделать репозиторий публичным

> **Зачем**: GitHub Actions для **public-репозиториев бесплатен и без лимитов**,
> биллинг не требуется (это важно при ограниченных способах оплаты).
> Для private репо нужны минуты Actions, привязанные к биллингу.
> В исходниках секретов нет — все читаются из `.env` / EasyPanel UI, в репу не попадают.

### 2.1 Перевести репо в public

В GitHub: **Settings → General → Danger Zone → Change repository visibility →
Make public**. Подтверди, введя имя репо.

После этого GitHub Actions сразу заработает без какого-либо биллинга.

### 2.2 Запустить первый билд

```bash
git add .
git commit -m "ci: switch deploy to prebuilt images on GHCR"
git push origin main
```

В GitHub открой вкладку **Actions** — должен появиться запуск
`Build and Push Docker Images`. Оба job'а (`build-api`, `build-frontend`) идут
параллельно на `ubuntu-latest`.

Длительность:
- **Первый билд**: 6–10 мин (без кэша, тянутся NuGet и npm пакеты)
- **Последующие билды на той же ветке**: 2–4 мин (благодаря `cache-from: type=gha`)

### 2.3 Если что-то пошло не так

| Симптом | Причина | Решение |
|---|---|---|
| Workflow не запускается | Actions выключены в Settings | Settings → Actions → General → Allow all actions |
| `denied: permission_denied: write_package` | Workflow без `packages: write` | Уже стоит в нашем workflow, но проверь права на репо |
| Build падает на `dotnet restore` | Сетевой сбой / NuGet rate-limit | Просто перезапусти job — `re-run failed jobs` |
| Очень долгий билд | Cache miss после крупного refactor'а | Норма — следующий билд будет быстрым |

## Шаг 3. Сделать пакеты публичными (или настроить registry credentials)

GHCR создаёт пакеты **приватными по умолчанию**. EasyPanel не сможет их pull без авторизации.

**Вариант A: сделать публичными (проще)**

В GitHub:
1. Профиль → **Packages**
2. Открой `wishhub-api` → **Package settings** → **Change visibility** → **Public**
3. Повтори для `wishhub-frontend`

Это безопасно: исходники приватного репо не утекают, в образ попадает только скомпилированное приложение. Секреты в образ не зашиты — все читаются из env.

**Вариант B: оставить приватными + настроить EasyPanel registry**

В EasyPanel: **Settings → Registries → Add** (`ghcr.io` + GitHub PAT с правом `read:packages`). Затем в проекте укажи этот registry.

## Шаг 4. Перенастроить EasyPanel на registry compose

В UI EasyPanel:

1. **Project → Settings → Source**
2. Поменяй **Compose file** с `docker-compose.prod.yml` на `docker-compose.registry.yml`
3. **Environment variables** — добавь две новые:
   ```
   IMAGE_OWNER=твой_github_username_lowercase
   IMAGE_TAG=latest
   ```
4. Все остальные переменные (`PUBLIC_URL`, `JWT_SECRET`, `POSTGRES_*`, `VK_*`, `TELEGRAM_*`) уже должны быть.
5. **Domains** — `wishhub.rabb1t.tech` → port `80` (порт frontend-контейнера)
6. **Deploy** / **Redeploy**

EasyPanel выполнит `docker compose pull` (быстро, ~30 сек) и `docker compose up -d` (мгновенно, образы уже на диске).

## Шаг 5. Дальнейшие апдейты

Обычный flow:

```bash
git push origin main
```

→ GitHub Actions собирают и пушат `:latest` (и `:sha-abc1234`)
→ EasyPanel автоматически (если включён Auto Deploy) или вручную через **Redeploy** делает `pull` + `up -d`.

Никакой сборки на сервере. Никакого build cache.

### Деплой конкретной версии

В EasyPanel поменяй `IMAGE_TAG=latest` на `IMAGE_TAG=sha-abc1234` или `IMAGE_TAG=v1.2.3` (если ты создал git tag), нажми Redeploy.

### Откат на предыдущую версию

```
IMAGE_TAG=sha-<предыдущий_sha>
→ Redeploy
```

Это занимает секунды — ничего не пересобирается.

## FAQ / Troubleshooting

**`docker compose pull` ругается "denied: requested access to the resource is denied"**

Пакет приватный, а EasyPanel не залогинен в GHCR. См. Шаг 3 — либо сделай пакет публичным, либо добавь registry в EasyPanel.

**Платформенный конфликт (`exec format error`)**

CI собирает только `linux/amd64`. Если у тебя ARM-сервер (редко, но бывает у некоторых VPS), либо добавь `linux/arm64` в `platforms:` workflow'а, либо запусти `scripts/build-and-push.sh` локально с `PLATFORM=linux/arm64`.

**API не стартует, в логах ошибка про playwright/Chromium**

Проверь что у контейнера достаточно памяти (Chromium стартует ~150 МБ RSS). На 1 ГБ VPS лучше включить swap или поднять до 2 ГБ.

**Хочу собирать локально, без GHCR**

Используй `docker-compose.prod.yml` (старая схема со сборкой) — он остался рабочим.

**На моей машине Docker занимает 13+ ГБ — это нормально?**

Да. На локальной dev-машине Docker накапливает:
- финальные images (~2-3 ГБ),
- BuildKit cache (~5-7 ГБ — он отдельный от images),
- volumes (БД, и т.п.),
- остановленные контейнеры.

Серверу EasyPanel ничего из этого не достанется — он только pull'ит готовый образ. Для локальной чистки:

```bash
# Снести build cache (безопасно, восстановится при следующем билде)
docker builder prune -af

# Снести dangling images (без тегов)
docker image prune -f

# Атомная: удалить ВСЁ что не используется (включая volumes — осторожно!)
docker system prune -af --volumes
```

Build cache даёт **прирост скорости** на повторных билдах, поэтому совсем без него
неудобно. Можно держать ~2-3 ГБ кэша как баланс.

## Размерный итог

| | До | После |
|---|---|---|
| API runtime image | ~1.81 ГБ | **~1.37 ГБ** |
| Frontend build image (промежуточный) | ~1.5 ГБ | ~500 МБ |
| Frontend runtime image | ~93 МБ | ~93 МБ |
| BuildKit cache на сервере (EasyPanel) | ~5–7 ГБ | **0** (сервер не собирает) |
| Время редеплоя в EasyPanel | 8–12 мин | **30–60 сек** (просто `pull`) |

> **Где осталось место**: ~542 МБ внутри API image — это сам Chromium-браузер,
> без которого парсеры не работают. Дальнейшее ужатие требует переключения парсеров на
> `chromium-headless-shell` (Channel = "chromium-headless-shell" в LaunchOptions) и
> установки только этого варианта в Dockerfile. Это сэкономит ~300 МБ, но может ослабить
> stealth-логику — оставлено как опциональное улучшение.
