# Конфигурация WishHub

Секреты хранятся в git-ignored `.env` для Compose либо `WishHub.Api/appsettings.Development.json` для локального API. В репозитории только шаблоны. Обычный `dotnet run` не читает корневой `.env` автоматически.

| Настройка | Назначение |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL; Compose строит строку из POSTGRES_DB/USER/PASSWORD |
| `Jwt__Secret`, `Jwt__Issuer`, `Jwt__Audience` | Подпись и проверка сессии |
| `Jwt__AccessTokenExpiryMinutes`, `Jwt__RefreshTokenExpiryDays` | Время жизни токенов; это не обновление товаров |
| `Frontend__Url` | Публичный адрес frontend/OAuth redirects |
| `OAuth__Vk__*`, `OAuth__Telegram__*` | Используемые провайдеры входа |
| `WishlistImages__StoragePath` | Папка собственных фото вне wwwroot; локально App_Data/wishlist-images, в Compose /app/App_Data/wishlist-images с volume wishlist_images |
| `Metadata__EnableOzonWbray` | false по умолчанию; экспериментальное имя/фото Ozon через внешний сервис |

В `.env` для Compose флаг называется `METADATA_ENABLE_OZON_WBRAY`; compose переводит его в `Metadata__EnableOzonWbray`. Настроек Redis, расписания цен, browser storage и CDP больше нет. Старые значения окружения можно удалить после обновления состава запуска.

Для EasyPanel заполните `.env.example`, выберите `IMAGE_OWNER` и опубликованный `IMAGE_TAG`. Не передавайте реальные ключи в git, команды примеров или клиентский bundle. Для ручного создания карточек внешние ключи не нужны.

Перед обновлением существующей установки прочитайте [порядок перехода](docs/lightweight-rollout.md).
