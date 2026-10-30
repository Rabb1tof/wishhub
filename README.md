# WishHub

Вишлист с редактируемыми карточками, друзьями, резервированием подарков и сообщениями.

Для карточки нужны только ссылка и имя. Фото и цена необязательны: их можно изменить или очистить. Кнопка «Получить данные» предлагает имя и фото по ссылке; ошибка источника не мешает сохранить форму вручную. Сохранение и просмотр списка не запускают парсинг или мониторинг цен.

## Получение данных

- Wildberries: лёгкие HTTP-запросы к карте CDN и JSON карточки, без браузера.
- Ozon: экспериментальный внешний источник WBRay, включаемый `Metadata__EnableOzonWbray=true`. По умолчанию отключён. Его endpoint публичной формы не является гарантированным API интеграции; проверьте условия и доступность перед включением.
- Яндекс Маркет и неизвестные магазины: ручное заполнение. Поисковый API с бесплатной квотой остаётся дополнительной задачей, не условием сохранения.
- Фото можно указать ссылкой или загрузить файлом JPEG, PNG, WebP до 5 МиБ. Есть предпросмотр, замена и удаление. Внешние картинки загружаются напрямую с `no-referrer`; собственные файлы выдаёт API с проверкой доступа к вишлисту. Недоступное фото показывает заглушку.

API-образ использует Alpine composite-extra с полноценными ICU и часовыми поясами. В runtime нет Chromium/Playwright, browser sidecar, Hangfire и Redis. Приложение использует ASP.NET Core/EF Core, PostgreSQL, React/Vite/TypeScript и SignalR. Аватары сохраняются в существующем volume; загруженные фото — в отдельном `wishlist_images`. Обработка изображений и генерация превью на сервере не выполняются.

## Локальная разработка

Требуются .NET 9 SDK, Node.js 24, PostgreSQL 16 либо Docker Desktop.

```sh
cp .env.example .env
# Заполните пароли, JWT и нужные OAuth-настройки.
docker compose up -d
dotnet run --project WishHub.Api
```

Перед запуском API скопируйте `WishHub.Api/appsettings.json.example` в git-ignored `WishHub.Api/appsettings.Development.json`, заполните настройки либо задайте соответствующие переменные окружения. Корневой `.env` читает Compose, а не обычный запуск dotnet. API применяет миграции при старте.

```sh
cd frontend
npm ci
npm run dev
```

Frontend: `http://localhost:5173`; API: порт из launchSettings/переменной `ASPNETCORE_URLS`.

## Контейнеры

Для опубликованных образов:

```sh
docker compose -f docker-compose.registry.yml -f docker-compose.local.yml up -d
```

Локальный override открывает frontend на 5173 и API на 5050. В EasyPanel используется только registry compose: домен направляется через Traefik, без публикации host-порта frontend.

`docker-compose.prod.yml` собирает образы на сервере и использует существующие nginx/TLS-настройки. PostgreSQL, аватары и загруженные фото сохраняются в volumes. Не используйте `down -v` для обновления.

## Документация

- [План и критерии](docs/lightweight-wishlist-plan.md)
- [Переход со старого API и runtime](docs/lightweight-rollout.md)
- [Конфигурация](CONFIGURATION.md)
- [Развёртывание](DEPLOY-OPTIMIZED.md)
- [Правила работы с кодом](AGENTS.md)
- [Настройка OAuth](docs/OAUTH_SETUP.md)
- [История изменений](CHANGELOG.md)
