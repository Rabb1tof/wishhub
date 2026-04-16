# WishHub Configuration Guide

> **Быстрый выбор**: EasyPanel → `EASYPANEL.md` | Docker Compose → `DEPLOY.md`

## Структура конфигурации

### Единый подход: один `.env` файл для всех сред

```
wishhub/
├── .env                          # ← Локальные секреты (НЕ коммитится!)
├── .env.example                    # ← Шаблон для всех сред (коммитится)
│
├── docker-compose.yml              # ← Локальная разработка
├── docker-compose.prod.yml         # ← Production (EasyPanel/VPS)
│
├── frontend/
│   ├── .env                        # ← Dev настройки (коммитится, дефолты)
│   ├── .env.local                  # ← Локальные оверрайды (НЕ коммитится)
│   └── .env.example                # ← Шаблон
│
└── WishHub.Api/
    ├── appsettings.json            # ← Базовый конфиг (коммитится)
    ├── appsettings.Development.json    # ← Локальный dev (НЕ коммитится)
    ├── appsettings.Production.json     # ← Production структура (НЕ коммитится)
    └── *.example.json              # ← Шаблоны (коммитятся)
```

---

## Файлы которые НЕ коммитятся (в `.gitignore`)

```bash
# Root
.env                          # ← Твои локальные секреты
.env.local, .env.prod        # ← Любые env с секретами
appsettings.Development.json # ← Dev конфиг .NET
appsettings.Production.json    # ← Prod конфиг .NET

# Frontend  
.env.local                    # ← Локальные оверрайды Vite
```

---

## Обязательные переменные окружения

Копируй из `.env.example` → `.env` и заполни:

```bash
# =============================================
# 1. Application URL
# =============================================
PUBLIC_URL=https://your_domain.com

# =============================================
# 2. Database (PostgreSQL)
# =============================================
POSTGRES_DB=wishlist
POSTGRES_USER=wishlist
# Генерация: openssl rand -base64 32
POSTGRES_PASSWORD=СИЛЬНЫЙ_ПАРОЛЬ

# =============================================
# 3. JWT Secret (критично!)
# =============================================
# Генерация: openssl rand -hex 32
JWT_SECRET=МИНИМУМ_32_СИМВОЛА_СЛУЧАЙНЫЕ
JWT_ISSUER=WishHub
JWT_AUDIENCE=WishHubUsers

# =============================================
# 4. OAuth VK ID
# =============================================
# Получить: https://id.vk.ru/business/go
# Redirect URI: ${PUBLIC_URL}/api/auth/vk/callback
VK_CLIENT_ID=ВАШ_VK_APP_ID
VK_CLIENT_SECRET=ВАШ_VK_SECRET

# =============================================
# 5. Telegram Bot
# =============================================
# Получить: @BotFather
# Настроить: /setdomain ${PUBLIC_DOMAIN}
TELEGRAM_BOT_TOKEN=ВАШ_ТОКЕН
TELEGRAM_BOT_USERNAME=ВАШ_БОТ

# =============================================
# 6. Background Jobs (опционально)
# =============================================
HANGFIRE_INTERVAL=8
```

---

## Сценарии использования

### 1. Локальная разработка (твой текущий setup)

```bash
# 1. Заполни .env своими секретами
cp .env.example .env
# Редактируй .env

# 2. Запусти инфраструктуру
docker-compose up -d postgres redis

# 3. Запусти backend
cd WishHub.Api && dotnet run

# 4. Запусти frontend (vite.config.ts тянет VITE_* из .env автоматически)
cd frontend && npm run dev
```

### 2. Локальная разработка через туннель (OAuth тестирование)

```bash
# В .env укажи туннельный URL
PUBLIC_URL=https://your_domain.com

# Обнови VK/Telegram настройки на этот домен
# Перезапусти frontend - он подхватит новый PUBLIC_URL из .env
npm run dev
```

### Синхронизация .env

Для production build frontend'а нужно синхронизировать .env:

```bash
# Скрипт скопирует PUBLIC_URL из root .env в frontend/.env.production
./scripts/sync-env.sh

# Или вручную:
cp .env frontend/.env.production
# Отредактируй, оставь только VITE_ переменные
```

### 3. Production через EasyPanel

```bash
# ВНИМАНИЕ: Не используй .env файл в EasyPanel!
# Все переменные добавляются через UI EasyPanel
```

Шаги:
1. Убедись что `.env` и `appsettings.Production.json` в `.gitignore`
2. Запушь код на GitHub
3. В EasyPanel: Create Project → Docker Compose
4. Добавь все переменные из `.env.example` в UI
5. Укажи домен: `your_domain.com`
6. SSL: Auto
7. Deploy

### 4. Production через Docker Compose (свой сервер)

```bash
# На сервере:
cp .env.example .env
# Заполни .env реальными значениями
nano .env

# Запусти
docker-compose -f docker-compose.prod.yml up -d
```

---

## Генерация секретов

```bash
# PostgreSQL пароль (32 байта в base64)
openssl rand -base64 32

# JWT Secret (32 байта в hex = 64 символа)
openssl rand -hex 32

# Или просто длинная случайная строка
date | md5 | base64
```

---

## Проверка перед коммитом

```bash
# Убедись что нет секретов в git
git status

# Проверь что .env в .gitignore
git check-ignore -v .env

# Проверь содержимое перед коммитом
git diff --cached
```

**Если случайно закоммитил секреты**: 
```bash
# Немедленно смени секреты!
# Потом: git filter-branch или BFG Repo-Cleaner
```

---

## Устранение неполадок

### "Environment variable not set"
- Проверь `.env` файл
- Перезапусти docker-compose: `docker-compose down && docker-compose up -d`

### OAuth "redirect_uri mismatch"
- Проверь `PUBLIC_URL` в `.env`
- Обнови настройки в VK ID / Telegram Bot
- Домен должен точно совпадать

### JWT ошибки
- Проверь длину `JWT_SECRET` (минимум 32 символа)
- Убедись что одинаковый ключ на всех инстансах

---

## Памятка по безопасности

✅ **Делай**:
- Храни `.env` отдельно от кода
- Используй разные JWT секреты для dev/prod
- Регулярно меняй пароли БД в production

❌ **НЕ делай**:
- Не коммить `.env` никогда
- Не используй дефолтные пароли в production
- Не храни секреты в коде
