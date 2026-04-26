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
- `wishhub-api:test` ≈ **550–650 МБ** (раньше было ~1.2 ГБ)
- `wishhub-frontend:test` ≈ **50–60 МБ** (без изменений — финальный stage уже был nginx:alpine)

Если размер API сильно больше 700 МБ — что-то пошло не так, проверь что в build-stage нет `--with-deps` (мы ставим apt-deps только в runtime).

## Шаг 2. Запушить изменения и дать GH Actions собрать

```bash
git add .
git commit -m "ci: switch deploy to prebuilt images on GHCR"
git push origin main
```

В GitHub открой вкладку **Actions** — должен появиться запуск `Build and Push Docker Images`. Должны успешно отработать оба job'а (`build-api`, `build-frontend`). Длительность первого билда — 5–10 минут (без кэша); последующие билды на том же `main` — 2–4 минуты благодаря `cache-from: type=gha`.

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

## Размерный итог

| | До | После |
|---|---|---|
| API runtime image | ~1.2 ГБ | ~600 МБ |
| Frontend build image (промежуточный) | ~1.5 ГБ | ~500 МБ |
| Frontend runtime image | ~55 МБ | ~55 МБ |
| BuildKit cache на сервере | ~5–7 ГБ | **0** (сервер не собирает) |
| Время редеплоя в EasyPanel | 8–12 мин | **30–60 сек** |
