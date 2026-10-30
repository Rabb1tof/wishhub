# WishHub API

ASP.NET Core и PostgreSQL. Конфигурация: [CONFIGURATION.md](../CONFIGURATION.md), переход существующей установки: [rollout](../docs/lightweight-rollout.md).

## Карточки

- `POST /api/wishlist`: обязательные `url`, `name`; необязательные `imageUrl`, `price`, `currency` (RUB по умолчанию).
- `PUT /api/wishlist/{id}`: полный набор содержимого карточки; null фото/цены очищает значение. Неуказанный sortOrder сохраняет порядок.
- DTO плоский: свойства карточки находятся рядом с id, sortOrder, createdAt и данными резервации, без обязательного Product.
- `POST /api/metadata/resolve`: авторизованное получение необязательных имени/фото по кнопке. Статусы ok/partial/unavailable/unsupported. Запрос не создаёт карточек или фоновых заданий.

Сохранение не вызывает внешние сервисы. Данные индивидуальны для WishlistItem; одинаковый товар у двух владельцев допускает разные ручные значения. Владелец не видит факт и автора резервирования своего подарка.

Имена/фото WB получает HTTP-адаптер. Ozon через WBRay включается `Metadata__EnableOzonWbray=true` после проверки условий использования. Маркет и неизвестные источники не блокируют ручное заполнение.

Нет Redis, Hangfire, browser runtime, `/api/test/parse` и старого endpoint принудительного перепарсинга. Auth refresh tokens, OAuth и SignalR продолжают выполнять свои функции.
