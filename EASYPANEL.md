# WishHub Deployment Guide for EasyPanel

> **⚠️ ВАЖНО**: Этот гайд для деплоя через EasyPanel (авто SSL, упрощённый процесс)
> Для обычного Docker Compose см. `DEPLOY.md`

## Быстрый старт (EasyPanel)

### 1. Подготовка репозитория

Убедись что в репозитории:
- ✅ `.env` и `appsettings.Production.json` в `.gitignore`
- ✅ Все секреты читаются из переменных окружения
- ✅ `docker-compose.prod.yml` адаптирован для единого `.env`

### 2. Создание проекта в EasyPanel

1. **Create Project** → выбери Docker Compose
2. **Repository**: твой GitHub/GitLab репозиторий
3. **Branch**: `main` или `master`

### 3. Настройка переменных окружения

В EasyPanel перейди в **Environment** и добавь все переменные из `.env.example`:

```bash
# Обязательные (заполни свои значения!)
PUBLIC_URL=https://your_domain.com

POSTGRES_DB=wishlist
POSTGRES_USER=wishlist
POSTGRES_PASSWORD=сгенерируй_сильный_пароль

JWT_SECRET=сгенерируй_через_openssl_rand_hex_32
JWT_ISSUER=WishHub
JWT_AUDIENCE=WishHubUsers

VK_CLIENT_ID=твой_vk_app_id
VK_CLIENT_SECRET=твой_vk_secret

TELEGRAM_BOT_TOKEN=твой_bot_token
TELEGRAM_BOT_USERNAME=твой_bot_username

HANGFIRE_INTERVAL=8
```

### 4. Настройка домена

1. В EasyPanel добавь домен: `wishhub.rabb1t.tech`
2. **SSL**: включи "Automatic HTTPS" (Let's Encrypt)
3. **Port**: укажи порт `80` (nginx внутри docker-compose)

### 5. Docker Compose для EasyPanel

EasyPanel автоматически подхватывает `docker-compose.yml` или `docker-compose.prod.yml`.

Если используешь `docker-compose.prod.yml` - убедись что он настроен на env-переменные:

```yaml
services:
  api:
    environment:
      - JWT__Secret=${JWT_SECRET}  # ← из EasyPanel env
      - OAuth__Vk__ClientId=${VK_CLIENT_ID}
      # ... и т.д.
```

### 6. Deploy

Нажми **Deploy** в EasyPanel.

После деплоя:
- Приложение доступно по `https://your_domain.com`
- SSL сертификат выдаётся автоматически

---

## 🔧 Важные моменты для EasyPanel

### Environment Variables Priority

EasyPanel передаёт env vars в контейнеры автоматически. Убедись что:
1. Все переменные из `.env.example` добавлены в EasyPanel UI
2. В `docker-compose.prod.yml` используется `${VAR}` синтаксис
3. В `appsettings.Production.json` стоят плейсхолдеры (реальные значения из env подтянутся Docker)

### VK OAuth Redirect URI

После деплоя на прод обнови в VK ID настройки:
- **Base domain**: `your_domain.com`
- **Redirect URI**: `https://your_domain.com/api/auth/vk/callback`

### Telegram Bot Domain

Через @BotFather выполни:
```
/setdomain
wishhub.rabb1t.tech
```

### Файл структуры env

Теперь используется **только один env файл** на все сервисы:

```
wishhub/
├── .env                      # ← Твой локальный dev (не коммитится!)
├── .env.example              # ← Шаблон для всех сред
│
├── docker-compose.yml        # ← Для локальной разработки
├── docker-compose.prod.yml   # ← Для production (EasyPanel/Docker)
│
└── WishHub.Api/
    ├── appsettings.json      # ← Базовый (коммитится)
    ├── appsettings.Production.json  # ← На сервере (не коммитится!)
    └── appsettings.Production.json.example  # ← Шаблон
```

### НЕ нужно для EasyPanel

❌ Ручная настройка SSL (авто от Let's Encrypt)
❌ Ручной запуск certbot
❌ Монтирование SSL сертификатов

### Нужно проверить

✅ Все env vars добавлены в EasyPanel UI
✅ VK/Telegram OAuth настроены на прод домен
✅ `.env` и `appsettings.Production.json` в `.gitignore`
✅ `appsettings.Production.json` не содержит реальных секретов (только структура)

---

## 🐛 Типичные проблемы EasyPanel

### "Environment variable not set"
- Проверь что переменная добавлена в EasyPanel UI
- Проверь что в `docker-compose.prod.yml` используется `${VAR_NAME}`

### OAuth не работает (redirect_uri mismatch)
- Проверь `PUBLIC_URL` в EasyPanel env
- Обнови redirect URI в VK/Telegram настроек
- Перезапусти деплой

### 502 Bad Gateway
- Проверь логи API контейнера в EasyPanel
- Убедись что порт 5000 открыт внутри сети Docker

---

## 🔄 Обновление приложения

В EasyPanel просто нажми **Redeploy** или включи Auto Deploy на push в ветку.

Для обновления env vars:
1. Измени в EasyPanel UI
2. Нажми **Redeploy**
