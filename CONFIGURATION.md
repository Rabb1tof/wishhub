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
| `ReverseProxy__KnownProxies`, `ReverseProxy__ForwardLimit` | Доверенные IP прокси через запятую и число переходов (по умолчанию 1) |
| `ReverseProxy__Enabled` | Признак установки за прокси для выбора общего лимита до настройки доверенных адресов |
| `Metadata__EnableOzonWbray` | false по умолчанию; экспериментальное имя/фото Ozon через внешний сервис |

В `.env` для Compose флаг называется `METADATA_ENABLE_OZON_WBRAY`; compose переводит его в `Metadata__EnableOzonWbray`. Настроек Redis, расписания цен, browser storage и CDP больше нет. Старые значения окружения можно удалить после обновления состава запуска.

Для EasyPanel заполните `.env.example`, выберите `IMAGE_OWNER` и опубликованный `IMAGE_TAG`. Не передавайте реальные ключи в git, команды примеров или клиентский bundle. Для ручного создания карточек внешние ключи не нужны.

Лимиты парольного входа и регистрации применяются отдельно: 12 и 5 запросов в минуту с одного IPv4-адреса или IPv6-подсети /64. Общий предел для этих маршрутов — 8 запросов одновременно на экземпляр API. Ответ 429 содержит `Retry-After` в секундах. Лимиты хранятся в памяти процесса. Очистка сессий удаляет до 500 уже истёкших записей при запуске и раз в 5 минут.

Для обеих конфигураций Compose укажите точные IP прокси через `WISHHUB_TRUSTED_PROXY_IPS` и число переходов через `WISHHUB_PROXY_HOPS`. Для цепочки Traefik → frontend nginx → API укажите оба IP и `WISHHUB_PROXY_HOPS=2`, если оба прокси добавляют фактический адрес своего клиента в `X-Forwarded-For`. После изменения адресов прокси обновите этот список. При пустом списке Compose использует общий лимит на экземпляр API: 60 запросов входа и 20 регистраций в минуту. Не включайте `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` для настройки лимитов: он отключает стандартный список доверенных прокси. Параметры TLS задаются отдельно.

Перед обновлением существующей установки прочитайте [порядок перехода](docs/lightweight-rollout.md).
