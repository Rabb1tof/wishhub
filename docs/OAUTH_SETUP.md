# OAuth setup (VK & Telegram)

Вся конфигурация задаётся через `appsettings.Development.json` / `appsettings.json`
или через environment variables. Формат env — двойные подчёркивания вместо `:`
(стандарт ASP.NET Core).

---

## 1. Frontend URL (редирект после OAuth-логина)

Бэк после успешного OAuth редиректит пользователя на `{Frontend:Url}/auth/callback?accessToken=...`.

```json
"Frontend": {
  "Url": "http://localhost:5173"
}
```

```env
Frontend__Url=http://localhost:5173
```

В проде — публичный URL фронта (например, `https://wishhub.app`).

---

## 2. VK OAuth

### Регистрация приложения

1. Зайти на https://dev.vk.com/ и создать **Веб-приложение** (Site, не Standalone).
2. В настройках приложения:
   - **Базовый домен:** домен фронта (в dev: `localhost`)
   - **Доверенный redirect URI:** URL callback бэка — в dev: `http://localhost:5000/api/auth/vk/callback`, в проде: `https://api.wishhub.app/api/auth/vk/callback`
3. Скопировать **Защищённый ключ** (`client_secret`) и **ID приложения** (`client_id`).

### Конфигурация

```json
"OAuth": {
  "Vk": {
    "ClientId": "12345678",
    "ClientSecret": "ваш_защищённый_ключ",
    "RedirectUri": "http://localhost:5000/api/auth/vk/callback"
  }
}
```

```env
OAuth__Vk__ClientId=12345678
OAuth__Vk__ClientSecret=ваш_защищённый_ключ
OAuth__Vk__RedirectUri=http://localhost:5000/api/auth/vk/callback
```

**Важно:** `RedirectUri` должен **точно** совпадать со значением в настройках приложения VK,
включая протокол и порт. Если не совпадает — VK вернёт `redirect_uri_mismatch`.

---

## 3. Telegram Login Widget

### Создание бота

1. В Telegram написать [@BotFather](https://t.me/BotFather), команда `/newbot`, выбрать имя и username.
   Username должен заканчиваться на `bot` (например, `WishHubAuthBot`).
2. BotFather выдаст **Bot Token** (выглядит как `123456789:ABCdefGhIJKlmnOPqrSTUvwxyz...`).
3. Настроить домен для Login Widget: написать BotFather `/setdomain` → выбрать бота → указать домен **фронта** (в dev: `localhost:5173`, в проде: `wishhub.app`).

### Конфигурация

```json
"OAuth": {
  "Telegram": {
    "BotToken": "123456789:ABCdefGhIJKlmnOPqrSTUvwxyz",
    "BotUsername": "WishHubAuthBot"
  }
}
```

```env
OAuth__Telegram__BotToken=123456789:ABCdefGhIJKlmnOPqrSTUvwxyz
OAuth__Telegram__BotUsername=WishHubAuthBot
```

**`BotUsername` — без `@` и без `_bot` суффикс менять не надо**, как есть из BotFather.

---

## 4. Docker Compose

Для прода передавайте переменные через `environment:` или `.env` файл:

```yaml
services:
  api:
    environment:
      Frontend__Url: https://wishhub.app
      OAuth__Vk__ClientId: ${VK_CLIENT_ID}
      OAuth__Vk__ClientSecret: ${VK_CLIENT_SECRET}
      OAuth__Vk__RedirectUri: https://api.wishhub.app/api/auth/vk/callback
      OAuth__Telegram__BotToken: ${TG_BOT_TOKEN}
      OAuth__Telegram__BotUsername: ${TG_BOT_USERNAME}
```

`.env` (рядом с `docker-compose.yml`):

```
VK_CLIENT_ID=12345678
VK_CLIENT_SECRET=...
TG_BOT_TOKEN=...
TG_BOT_USERNAME=WishHubAuthBot
```

---

## 5. Как это работает

### Кнопки на LoginPage

Фронт делает `GET /api/auth/providers` — публичный эндпоинт, возвращает:

```json
{ "vk": true, "telegramBotUsername": "WishHubAuthBot" }
```

Если `vk: false` — кнопка VK не показывается.
Если `telegramBotUsername: null` — виджет Telegram не показывается.

### VK flow (login)

1. Клик на «Войти через VK» → `window.location.href = '/api/auth/vk'`
2. Бэк ставит cookie `vk_oauth_state`, редиректит на `https://oauth.vk.com/authorize?...`
3. VK редиректит на `/api/auth/vk/callback?code=...&state=...`
4. Бэк проверяет state, меняет code на access_token + user_id
5. Если пользователь существует — логиним; если нет — создаём
6. Бэк редиректит на `{Frontend:Url}/auth/callback?accessToken=...&refreshToken=...&expiresAt=...`
7. Фронт-страница `/auth/callback` сохраняет токены в Zustand, грузит профиль через `/users/me`, редиректит на `/`

### VK flow (link)

1. В `/settings` клик «Привязать VK»
2. Фронт: `POST /api/auth/link/vk/start` с JWT → бэк ставит куки `vk_oauth_state` + `vk_oauth_link_user` (подписанный HMAC `userId`), возвращает `{ url }`
3. `window.location.href = url` → тот же VK flow
4. В callback, если есть `vk_oauth_link_user` — привязываем к существующему пользователю и редиректим на `/settings?linked=vk`

### Telegram flow (login)

1. Telegram Login Widget (iframe на `telegram.org`) после клика пользователя вызывает JS-callback с подписанными данными
2. Фронт отправляет `POST /api/auth/telegram` с этими данными
3. Бэк проверяет HMAC-SHA256 подпись с bot token, создаёт/находит пользователя, возвращает AuthResponse
4. Фронт сохраняет токены в Zustand

### Telegram flow (link)

То же самое, но `POST /api/auth/link/telegram` с JWT — бэк привязывает к текущему пользователю.

---

## 6. Troubleshooting

| Код ошибки в URL | Причина |
|---|---|
| `vk_not_configured` | VK client id/secret не заданы |
| `vk_token_failed` | VK API вернул ошибку при обмене code → token |
| `vk_no_token` | VK не вернул access_token |
| `invalid_state` | Cookie state не совпадает с query state (истекла сессия / cross-domain) |
| `vk_already_linked` | Этот VK аккаунт уже привязан к другому пользователю |
| `user_create_failed` | ASP.NET Identity отклонил создание (обычно из-за конфликта username/email) |

**VK в dev не отдаёт токен?** Проверь `RedirectUri` — должен совпадать **байт-в-байт** с настройкой в dev.vk.com.

**Telegram widget не появляется?** Проверь что `BotUsername` правильный и что домен задан через `/setdomain` в @BotFather. Widget не работает на `http://localhost` без `/setdomain localhost`.
