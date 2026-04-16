# WishHub Backend API

ASP.NET Core 9 Web API для WishHub - сервиса управления вишлистами с парсингом товаров с Ozon, Wildberries и Яндекс.Маркет.

## Стек

- **.NET 9** - основной фреймворк
- **Entity Framework Core 9** - ORM
- **PostgreSQL 16** - база данных
- **Redis 7** - кэш и SignalR бэкплейн
- **ASP.NET Core Identity** - аутентификация
- **JWT Bearer** - токены доступа
- **SignalR** - real-time чат
- **Hangfire** - фоновые задачи (обновление цен)
- **Microsoft Playwright** - парсинг товаров
- **HtmlAgilityPack** - HTML парсинг

## Быстрый старт

### Предварительные требования

- .NET 9 SDK
- Docker + Docker Compose
- `dotnet-ef` tool: `dotnet tool install --global dotnet-ef`

### 1. Инфраструктура

```bash
cd .. # в корень проекта
docker-compose up -d
```

Поднимет PostgreSQL (порт 5432) и Redis (порт 6379).

### 2. Конфигурация

Скопируй `appsettings.json` в `appsettings.Development.json` и заполни секреты:

```bash
cp appsettings.json appsettings.Development.json
```

Обязательно измени:

```json
{
  "Jwt": {
    "Secret": "YOUR-VERY-LONG-RANDOM-SECRET-KEY-MIN-32-CHARS"
  },
  "OAuth": {
    "Vk": {
      "ClientId": "YOUR_VK_APP_ID",
      "ClientSecret": "YOUR_VK_SECRET",
      "RedirectUri": "https://your-domain.com/api/auth/vk/callback"
    },
    "Telegram": {
      "BotToken": "YOUR_BOT_TOKEN",
      "BotUsername": "your_bot_username"
    }
  }
}
```

**⚠️ ВАЖНО**: `appsettings.Development.json` уже в `.gitignore` - никогда не коммить реальные секреты!

### 3. Миграции базы данных

```bash
dotnet ef database update \
  --project ../WishHub.Infrastructure \
  --startup-project .
```

### 4. Запуск

```bash
dotnet run
```

API будет доступен на:
- `http://localhost:5000` - HTTP
- `https://localhost:5001` - HTTPS (если настроен сертификат)

## Структура проекта

```
WishHub.Api/
├── Controllers/          # API endpoints
│   ├── OAuthController.cs    # VK/Telegram OAuth
│   ├── ProductsController.cs # Товары и парсинг
│   ├── WishlistController.cs # Вишлисты
│   └── ...
├── Hubs/                # SignalR real-time
│   └── ChatHub.cs       # Чат между пользователями
├── Middleware/          # Кастомные middleware
├── Extensions/          # DI расширения
├── appsettings.json     # Базовая конфигурация (без секретов)
└── Program.cs           # Точка входа
```

## Окружения

| Файл | Назначение |
|------|-----------|
| `appsettings.json` | Базовая конфигурация, коммитится в git |
| `appsettings.Development.json` | Локальная разработка, **НЕ коммитится** |
| `appsettings.Production.json` | Production, **НЕ коммитится** |

## OAuth Настройка

### VK ID

1. Зарегистрируй приложение: https://id.vk.ru/business/go
2. Тип: **Web**
3. Базовый домен: `your-domain.com` (без https://)
4. Доверенный redirect URL: `https://your-domain.com/api/auth/vk/callback`
5. Скопируй Client ID и Secret в `appsettings.Development.json`

### Telegram

1. Создай бота через @BotFather
2. Команда `/setdomain` и укажи свой домен
3. Скопируй токен и username (без @) в конфиг

## JWT Secret Key

### Что это?

Секретный ключ для подписи JWT токенов. Используется для:
- Генерации access token при логине
- Валидации токенов в защищённых endpoint
- Подписи временных ссылок (OAuth state, привязка аккаунтов)

### Требования

- **Минимум 32 символа** (256 бит для HMAC-SHA256)
- Случайные символы (буквы, цифры, спецсимволы)
- Должен быть одинаковым на всех инстансах приложения

### Как сгенерировать

**Linux/macOS:**
```bash
openssl rand -base64 48
# или
openssl rand -hex 32
```

**PowerShell:**
```powershell
-join ((48..57 + 65..90 + 97..122) | Get-Random -Count 48 | % {[char]$_})
```

**C# (для проверки):**
```csharp
Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
```

### Нужно ли менять?

| Когда | Действие |
|-------|----------|
| Новый проект | Сгенерируй свой уникальный ключ |
| Production | Обязательно замени dev-ключ на случайный |
| Утечка ключа | Срочно смени - все токены станут невалидны |
| Ротация | Можно менять периодически, но пользователи разлогинятся |

### Рекомендации по безопасности

1. **Никогда** не используй ключ из примера (`dev-secret-key...`) в production
2. Храни ключ в переменных окружения или секретном хранилище
3. Для production используй: `dotnet user-secrets` или Azure Key Vault / AWS Secrets Manager
4. Разные ключи для разных окружений (dev, staging, prod)

## Дополнительно

- **Фоновые задачи**: Hangfire Dashboard доступен на `/hangfire`
- **API документация**: Swagger UI на `/swagger` (только в Development)
- **Health checks**: `/health` и `/health/ready`

## Полезные команды

```bash
# Добавить миграцию
dotnet ef migrations add MigrationName \
  --project ../WishHub.Infrastructure \
  --startup-project .

# Удалить последнюю миграцию (если не применена)
dotnet ef migrations remove \
  --project ../WishHub.Infrastructure \
  --startup-project .

# Создать production билд
dotnet publish -c Release -o ./publish

# Запуск тестов
dotnet test ../WishHub.Tests
```
