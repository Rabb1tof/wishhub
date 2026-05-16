# Wishlist App — Детальный план реализации

> Каждый пункт — конкретная задача. Отмечай `[x]` когда выполнено.
> Порядок внутри этапа важен — не переходи к следующему пункту, не завершив предыдущий.

---

## Стек

| Слой | Технология |
|---|---|
| Backend | .NET 9, ASP.NET Core Web API |
| ORM | Entity Framework Core 9 |
| БД | PostgreSQL 16 |
| Кэш | Redis 7 |
| Парсинг JS-страниц | Microsoft.Playwright |
| Фоновые задачи | Hangfire + Hangfire.PostgreSql |
| Real-time | ASP.NET Core SignalR |
| Аутентификация | ASP.NET Core Identity + JWT Bearer |
| Frontend | React 18, Vite, TypeScript |
| HTTP-клиент (FE) | Axios + TanStack Query v5 |
| Стейт (FE) | Zustand |
| Роутинг (FE) | React Router v6 |
| Контейнеризация | Docker + Docker Compose |

---

## Этап 1 — Инфраструктура и структура решения

### 1.1 Solution и проекты

- [x] Создать папку `wishlist/`
- [x] `dotnet new sln -n WishHub`
- [x] `dotnet new webapi -n WishHub.Api --no-openapi false` → добавить в sln
- [x] `dotnet new classlib -n WishHub.Core` → добавить в sln
- [x] `dotnet new classlib -n WishHub.Infrastructure` → добавить в sln
- [x] `dotnet new classlib -n WishHub.Parsing` → добавить в sln
- [x] `dotnet new xunit -n WishHub.Tests` → добавить в sln
- [x] Добавить project references:
  - `WishHub.Api` → `WishHub.Infrastructure`, `WishHub.Parsing`
  - `WishHub.Infrastructure` → `WishHub.Core`
  - `WishHub.Parsing` → `WishHub.Core`
  - `WishHub.Tests` → все проекты
- [x] Создать `frontend/` через `npm create vite@latest frontend -- --template react-ts`

### 1.2 Структура папок

```
wishlist/
├── WishHub.Api/
│   ├── Controllers/
│   ├── Middleware/
│   ├── Extensions/          # регистрация сервисов
│   └── Program.cs
├── WishHub.Core/
│   ├── Entities/            # доменные модели
│   ├── Interfaces/          # IRepository, IProductParser, IAvatarStorage...
│   ├── DTOs/
│   └── Exceptions/
├── WishHub.Infrastructure/
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   ├── Configurations/  # IEntityTypeConfiguration<T>
│   │   └── Migrations/
│   ├── Repositories/
│   ├── Services/            # реализации интерфейсов из Core
│   └── BackgroundJobs/
├── WishHub.Parsing/
│   ├── Parsers/
│   │   ├── IProductParser.cs
│   │   ├── WildberriesParser.cs
│   │   ├── OzonParser.cs
│   │   └── YandexMarketParser.cs
│   ├── ParserFactory.cs
│   └── Models/ParseResult.cs
├── WishHub.Tests/
│   ├── Parsing/
│   ├── Api/
│   └── Infrastructure/
└── frontend/
    └── src/
        ├── api/
        ├── components/
        ├── pages/
        ├── store/
        ├── hooks/
        └── types/
```

- [x] Создать все папки выше (пустые `.gitkeep` где нужно)

### 1.3 Docker Compose

- [x] Создать `docker-compose.yml` в корне:

```yaml
version: '3.9'
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: wishlist
      POSTGRES_USER: wishlist
      POSTGRES_PASSWORD: wishlist
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"

volumes:
  pgdata:
```

- [x] `docker-compose up -d` — файл создан (запускается пользователем при необходимости)

### 1.4 NuGet-зависимости

Установить в нужные проекты:

- [x] `WishHub.Infrastructure`:
  - `Microsoft.EntityFrameworkCore` 9.x
  - `Npgsql.EntityFrameworkCore.PostgreSQL` 9.x
  - `Microsoft.EntityFrameworkCore.Design`
  - `Hangfire.Core`
  - `Hangfire.PostgreSql`
  - `StackExchange.Redis`
  - `Microsoft.Extensions.Caching.StackExchangeRedis`
- [x] `WishHub.Api`:
  - `Microsoft.AspNetCore.Authentication.JwtBearer`
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
  - `Swashbuckle.AspNetCore`
- [x] `WishHub.Parsing`:
  - `Microsoft.Playwright`
  - `HtmlAgilityPack`
  - `System.Text.Json`
- [x] `WishHub.Tests`:
  - `Microsoft.AspNetCore.Mvc.Testing`
  - `Testcontainers.PostgreSql`
  - `FluentAssertions`
  - `NSubstitute`

### 1.5 Конфигурация приложения

- [x] `WishHub.Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Database=wishlist;Username=wishlist;Password=wishlist",
    "Redis": "localhost:6379"
  },
  "Jwt": {
    "Secret": "REPLACE_WITH_32+_CHAR_SECRET",
    "Issuer": "wishlist-api",
    "Audience": "wishlist-client",
    "AccessTokenExpiryMinutes": 60,
    "RefreshTokenExpiryDays": 30
  },
  "Hangfire": {
    "PriceUpdateIntervalHours": 8
  },
  "ImageProxy": {
    "CacheTtlHours": 48,
    "RequestTimeoutSeconds": 10
  }
}
```

- [x] `appsettings.Development.json` — переопределить строки подключения если нужно
- [x] Добавить `.env` в `.gitignore`

---

## Этап 2 — Доменные модели и база данных

### 2.1 Сущности (WishHub.Core/Entities/)

- [x] `User.cs`:
```csharp
public class User : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public WishlistPrivacy WishlistPrivacy { get; set; } = WishlistPrivacy.Public;

    public ICollection<WishlistItem> WishlistItems { get; set; } = [];
    public ICollection<UserExternalLogin> ExternalLogins { get; set; } = [];
    public ICollection<Friendship> SentFriendRequests { get; set; } = [];
    public ICollection<Friendship> ReceivedFriendRequests { get; set; } = [];
    public ICollection<Message> SentMessages { get; set; } = [];
    public ICollection<Message> ReceivedMessages { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
}

public enum WishlistPrivacy { Public, FriendsOnly, Private }
```

- [x] `UserExternalLogin.cs`:
```csharp
public class UserExternalLogin
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;  // "vk" | "telegram"
    public string ExternalId { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public DateTime LinkedAt { get; set; }

    public User User { get; set; } = null!;
}
```

- [x] `Product.cs`:
```csharp
public class Product
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public ProductSource Source { get; set; }
    public DateTime LastParsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<WishlistItem> WishlistItems { get; set; } = [];
}

public enum ProductSource { Unknown, Wildberries, Ozon, YandexMarket }
```

- [x] `WishlistItem.cs`:
```csharp
public class WishlistItem
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid ProductId { get; set; }
    public string? CustomName { get; set; }
    public bool IsReserved { get; set; }
    public Guid? ReservedById { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public User Owner { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public User? ReservedBy { get; set; }
}
```

- [x] `Friendship.cs`:
```csharp
public class Friendship
{
    public Guid Id { get; set; }
    public Guid RequesterId { get; set; }
    public Guid AddresseeId { get; set; }
    public FriendshipStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public User Requester { get; set; } = null!;
    public User Addressee { get; set; } = null!;
}

public enum FriendshipStatus { Pending, Accepted, Blocked }
```

- [x] `Message.cs`:
```csharp
public class Message
{
    public Guid Id { get; set; }
    public Guid SenderId { get; set; }
    public Guid ReceiverId { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }

    public User Sender { get; set; } = null!;
    public User Receiver { get; set; } = null!;
}
```

- [x] `Notification.cs`:
```csharp
public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public string Payload { get; set; } = "{}";  // JSON
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}

public enum NotificationType
{
    PriceChanged,
    FriendRequest,
    FriendRequestAccepted,
    ItemReserved,
    NewMessage
}
```

- [x] `RefreshToken.cs`:
```csharp
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
```

### 2.2 DbContext (WishHub.Infrastructure/Data/)

- [x] `AppDbContext.cs`:
```csharp
public class AppDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<UserExternalLogin> ExternalLogins => Set<UserExternalLogin>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
```

### 2.3 Конфигурации EF (WishHub.Infrastructure/Data/Configurations/)

- [x] `UserConfiguration.cs` — индексы на `UserName`, `Email`; ограничения длин
- [x] `ProductConfiguration.cs` — уникальный индекс на `Url`; индекс на `Source`
- [x] `WishlistItemConfiguration.cs` — составной индекс `(OwnerId, ProductId)`; каскадное удаление при удалении владельца
- [x] `FriendshipConfiguration.cs` — составной уникальный индекс `(RequesterId, AddresseeId)`; индекс на `Status`
- [x] `MessageConfiguration.cs` — индекс на `(SenderId, ReceiverId, CreatedAt)`
- [x] `NotificationConfiguration.cs` — индекс на `(UserId, IsRead, CreatedAt)`
- [x] `RefreshTokenConfiguration.cs` — уникальный индекс на `Token`; индекс на `UserId`
- [x] `UserExternalLoginConfiguration.cs` — уникальный индекс на `(Provider, ExternalId)`

### 2.4 Первичная миграция

- [x] `dotnet ef migrations add InitialCreate --project WishHub.Infrastructure --startup-project WishHub.Api`
- [x] Проверить сгенерированный SQL (`dotnet ef migrations script`)
- [ ] `dotnet ef database update --project WishHub.Infrastructure --startup-project WishHub.Api`

---

## Этап 3 — Аутентификация (JWT + Identity)

### 3.1 Регистрация сервисов (WishHub.Api/Extensions/)

- [x] `ServiceCollectionExtensions.cs` — метод `AddIdentityServices(this IServiceCollection services, IConfiguration config)`:
  - `AddIdentity<User, IdentityRole<Guid>>()` с настройками пароля (minLength: 8, requireDigit: true)
  - `AddEntityFrameworkStores<AppDbContext>()`
  - `AddAuthentication(JwtBearerDefaults.AuthenticationScheme)`
  - `AddJwtBearer(...)` — настроить `TokenValidationParameters` из конфига

### 3.2 Вспомогательные сервисы

- [x] `WishHub.Core/Interfaces/ITokenService.cs`:
```csharp
public interface ITokenService
{
    string GenerateAccessToken(User user);
    RefreshToken GenerateRefreshToken(Guid userId);
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}
```

- [x] `WishHub.Infrastructure/Services/TokenService.cs` — реализация:
  - `GenerateAccessToken` — claims: `sub` (userId), `email`, `username`, exp = AccessTokenExpiryMinutes
  - `GenerateRefreshToken` — `Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))`, exp = RefreshTokenExpiryDays
  - `GetPrincipalFromExpiredToken` — валидировать без проверки времени

### 3.3 DTOs (WishHub.Core/DTOs/Auth/)

- [x] `RegisterRequest.cs` — `Username`, `Email`, `Password`, `DisplayName`
- [x] `LoginRequest.cs` — `UsernameOrEmail`, `Password`
- [x] `AuthResponse.cs` — `AccessToken`, `RefreshToken`, `ExpiresAt`, `User` (вложенный `UserDto`)
- [x] `RefreshRequest.cs` — `RefreshToken`

### 3.4 AuthController (WishHub.Api/Controllers/)

- [x] `POST /api/auth/register`:
  - Валидировать `RegisterRequest` (DataAnnotations или FluentValidation)
  - `UserManager.CreateAsync(user, password)`
  - Вернуть `AuthResponse` с токенами

- [x] `POST /api/auth/login`:
  - Найти пользователя по email или username
  - `UserManager.CheckPasswordAsync`
  - Сохранить `RefreshToken` в БД
  - Вернуть `AuthResponse`

- [x] `POST /api/auth/refresh`:
  - Найти `RefreshToken` в БД, проверить `IsRevoked` и `ExpiresAt`
  - Отозвать старый, выдать новый
  - Вернуть новый `AuthResponse`

- [x] `POST /api/auth/logout` `[Authorize]`:
  - Отозвать текущий `RefreshToken` пользователя

### 3.5 Middleware

- [x] `WishHub.Api/Middleware/ExceptionHandlingMiddleware.cs`:
  - Перехватывать необработанные исключения
  - Возвращать `ProblemDetails` с нужным HTTP-кодом
  - Логировать через `ILogger`

- [x] Зарегистрировать middleware в `Program.cs` до `UseAuthentication()`

### 3.6 Program.cs (минимальная версия)

- [x] `builder.Services.AddDbContext<AppDbContext>(...)`
- [x] `builder.Services.AddStackExchangeRedisCache(...)`
- [x] `builder.Services.AddIdentityServices(builder.Configuration)`
- [x] `builder.Services.AddControllers()`
- [x] `builder.Services.AddEndpointsApiExplorer()`
- [x] `builder.Services.AddSwaggerGen(...)` — с поддержкой Bearer-токена
- [x] `app.UseMiddleware<ExceptionHandlingMiddleware>()`
- [x] `app.UseAuthentication()` → `app.UseAuthorization()`
- [x] `app.MapControllers()`

---

## Этап 4 — Парсинг товаров

### 4.1 Модели парсинга (WishHub.Parsing/Models/)

- [x] `ParseResult.cs`:
```csharp
public class ParseResult
{
    public bool Success { get; set; }
    public string? Name { get; set; }
    public string? ImageUrl { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string? ErrorMessage { get; set; }
    public ProductSource Source { get; set; }
}
```

### 4.2 Интерфейс парсера

- [x] `WishHub.Core/Interfaces/IProductParser.cs`:
```csharp
public interface IProductParser
{
    bool CanParse(string url);
    Task<ParseResult> ParseAsync(string url, CancellationToken ct = default);
}
```

### 4.3 Wildberries Parser

WB предоставляет открытый API карточек товаров.

- [x] `WildberriesParser.cs`:
  - `CanParse` — `url.Contains("wildberries.ru")`
  - Извлечь `nmId` (артикул) из URL регуляркой: `\/(\d+)\/?`
  - `GET https://card.wb.ru/cards/v2/detail?appType=1&curr=rub&dest=-1257786&nm={nmId}`
  - Распарсить JSON: `data.products[0]` → `name`, `salePriceU / 100` (цена в копейках), `photos[0].big` (картинка)
  - Обработать случай когда товар не найден (пустой `products`)

### 4.4 Ozon Parser

Ozon усложнён — используют GraphQL и проверяют заголовки.

- [x] `OzonParser.cs` — попытка через HTTP:
  - `CanParse` — `url.Contains("ozon.ru")`
  - Попробовать `GET` с заголовками: `User-Agent`, `Accept`, `Accept-Language`, `Referer`
  - Если ответ пришёл — распарсить `HtmlAgilityPack`: `og:title`, `og:image`, meta price
  
- [x] Fallback через Playwright если HTML-парсинг не дал цену:
  - `IPage.GotoAsync(url)` → `WaitForSelectorAsync(".price-number")` (или актуальный селектор)
  - Извлечь цену и название из DOM

- [x] Обернуть Playwright-часть в `try/catch` — если не получилось, вернуть `Success = false`

### 4.5 Yandex Market Parser

- [x] `YandexMarketParser.cs`:
  - `CanParse` — `url.Contains("market.yandex.ru")`
  - Попытка через HTTP + `HtmlAgilityPack`: `og:title`, `og:image`, `[data-auto="price-value"]`
  - Fallback через Playwright при необходимости

### 4.6 ParserFactory

- [x] `WishHub.Core/Services/ParserFactory.cs`:
```csharp
public class ParserFactory
{
    private readonly IEnumerable<IProductParser> _parsers;

    public ParserFactory(IEnumerable<IProductParser> parsers)
        => _parsers = parsers;

    public IProductParser? GetParser(string url)
        => _parsers.FirstOrDefault(p => p.CanParse(url));
}
```

- [x] Зарегистрировать все парсеры и фабрику в DI в `Program.cs`

### 4.7 ProductService (WishHub.Infrastructure/Services/)

- [x] `IProductService.cs`:
```csharp
public interface IProductService
{
    Task<Product> GetOrCreateFromUrlAsync(string url, CancellationToken ct = default);
    Task RefreshPriceAsync(Guid productId, CancellationToken ct = default);
    Task RefreshAllStaleProductsAsync(CancellationToken ct = default);
}
```

- [x] `ProductService.cs`:
  - `GetOrCreateFromUrlAsync`:
    1. Найти в БД `Products` по `Url`
    2. Если найден и `LastParsedAt > DateTime.UtcNow - 6h` — вернуть существующий
    3. Иначе — вызвать `ParserFactory.GetParser(url).ParseAsync(url)`
    4. Создать или обновить запись в `Products`
    5. Если цена изменилась — создать `Notification` типа `PriceChanged` всем владельцам с этим товаром
  - `RefreshAllStaleProductsAsync` — загрузить все `Products` где `LastParsedAt < now - 8h`, обновить каждый

### 4.8 Playwright setup

- [ ] В `WishHub.Parsing.csproj` добавить:
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Playwright" Version="*" />
</ItemGroup>
```
- [ ] В CI/CD и Dockerfile добавить шаг: `playwright install chromium`
- [ ] Создать `IPlaywrightBrowserPool` — синглтон, держит один `IBrowser`, переиспользует страницы

### 4.9 Ozon stealth scraping hardening

- [ ] Добавить `FingerprintProfile`, `IFingerprintPool`, `FingerprintPool` с реалистичными профилями Chrome и согласованными параметрами экрана/платформы
- [ ] Добавить фабрику stealth init script с маскировкой `navigator.webdriver`, `plugins`, `languages`, `permissions`, WebGL и profile-driven hardware fields
- [ ] Добавить `IBrowserContextFactory`/`BrowserContextFactory` для stealth-контекстов с profile-driven headers и опциональным proxy
- [ ] Добавить `ISessionManager`/`SessionManager` для warm-up и сохранения/восстановления storage state Ozon-сессий
- [ ] Добавить `IHumanBehaviorSimulator`/`HumanBehaviorSimulator` с jitter, scroll и mouse movement
- [ ] Добавить `IBlockDetector`/`BlockDetector` и `OzonBlockedException` для детекта CAPTCHA/blocked responses
- [ ] Добавить `IOzonProductScraper`/`OzonProductScraper` с retry по новому profile/proxy и извлечением расширенных полей товара
- [ ] Добавить `IRequestScheduler`/`RequestScheduler` для batch scraping с лимитом параллелизма и lock per proxy
- [ ] Обновить `OzonParser` и DI-регистрацию для использования нового stealth pipeline
- [ ] Добавить unit/integration tests для новых Ozon stealth компонентов

---

## Этап 5 — Прокси картинок

### 5.1 ImageProxyController

- [x] `WishHub.Api/Controllers/ImageProxyController.cs`:
```
GET /api/images/proxy?url={encodedUrl}
```
- Декодировать `url`
- Проверить что домен входит в whitelist: `images.wbstatic.net`, `*.ozon.ru`, `avatars.mds.yandex.net`
- Проверить Redis-кэш по ключу `imgproxy:{sha256(url)}`
  - Если есть в кэше — вернуть сохранённый Content-Type + bytes
  - Если нет — `HttpClient.GetAsync(url)` с таймаутом 10s
  - Сохранить в Redis с TTL 48h
  - Вернуть `File(bytes, contentType)`
- При ошибке (404, таймаут) — вернуть `404` с телом `{ "error": "image_unavailable" }`

### 5.2 Whitelist доменов

- [x] Вынести список разрешённых доменов в `appsettings.json`:
```json
"ImageProxy": {
  "AllowedDomains": [
    "images.wbstatic.net",
    "basket-01.wb.ru",
    "cdn1.ozon.ru",
    "avatars.mds.yandex.net"
  ],
  "CacheTtlHours": 48,
  "RequestTimeoutSeconds": 10
}
```

- [x] Зарегистрировать именованный `HttpClient` для прокси с нужными заголовками (`User-Agent`, `Referer`)

---

## Этап 6 — Wishlist API

### 6.1 DTOs (WishHub.Core/DTOs/Wishlist/)

- [x] `AddWishlistItemRequest.cs` — `Url` (required), `CustomName?`
- [x] `UpdateWishlistItemRequest.cs` — `CustomName?`, `SortOrder?`
- [x] `WishlistItemDto.cs` — `Id`, `CustomName`, `IsReserved`, `IsReservedByMe`, `SortOrder`, `CreatedAt`, `Product: ProductDto`
- [x] `ProductDto.cs` — `Id`, `Name`, `ImageProxyUrl`, `Price`, `Currency`, `Source`
  - `ImageProxyUrl` = `/api/images/proxy?url={UrlEncode(product.ImageUrl)}`

### 6.2 WishlistController

- [x] `POST /api/wishlist` `[Authorize]`:
  1. Валидировать URL (Uri.TryCreate + схема http/https)
  2. `ProductService.GetOrCreateFromUrlAsync(url)`
  3. Проверить что у пользователя нет дубля (тот же `ProductId`)
  4. Создать `WishlistItem`
  5. Вернуть `WishlistItemDto` (201 Created)

- [x] `GET /api/wishlist` `[Authorize]` — вернуть свой вишлист:
  - Query params: `page`, `pageSize` (default 20), `minPrice?`, `maxPrice?`, `source?`, `sort` (date/price/name)
  - Cursor-based пагинация по `(SortOrder, Id)`

- [ ] `GET /api/users/{username}/wishlist` — публичный вишлист:
  - Проверить `WishlistPrivacy` пользователя
  - Если `FriendsOnly` — проверить `Friendship.Status == Accepted` с текущим пользователем
  - Если `Private` — вернуть `403`
  - Те же фильтры и пагинация
  - Для чужого вишлиста: `IsReservedByMe` = текущий пользователь является `ReservedById`; `IsReserved` показывать только если это не сам владелец (владелец не должен видеть кто что зарезервировал)

- [x] `PUT /api/wishlist/{itemId}` `[Authorize]`: (PATCH → PUT для простоты)
  - Проверить что `OwnerId == currentUserId`
  - Обновить `CustomName`, `SortOrder`

- [x] `DELETE /api/wishlist/{itemId}` `[Authorize]`:
  - Проверить что `OwnerId == currentUserId`
  - Снять резервацию если была
  - Удалить

- [x] `POST /api/wishlist/{itemId}/reserve` `[Authorize]`:
  - Нельзя резервировать свой товар
  - Если уже зарезервирован — вернуть `409`
  - Установить `IsReserved = true`, `ReservedById = currentUserId`
  - Создать `Notification` владельцу типа `ItemReserved`

- [x] `POST /api/wishlist/{itemId}/cancel-reservation` `[Authorize]`: (DELETE → POST)
  - Только тот кто зарезервировал может снять
  - Сбросить `IsReserved`, `ReservedById`

---

## Этап 7 — Профили пользователей

### 7.1 DTOs (WishHub.Core/DTOs/Users/)

- [x] `UserProfileDto.cs` — `Id`, `Username`, `DisplayName`, `Bio`, `AvatarUrl`, `CreatedAt`, `FriendshipStatus?`
- [x] `UpdateProfileRequest.cs` — `DisplayName?`, `Bio?`, `WishlistPrivacy?`
- [x] `ChangePasswordRequest.cs` — `CurrentPassword`, `NewPassword`

### 7.2 UsersController

- [x] `GET /api/users/{username}`:
  - Найти пользователя по `UserName`
  - Если не найден — `404`
  - Вернуть `UserProfileDto` (включая `FriendshipStatus` если запрос аутентифицирован)

- [x] `PUT /api/users/me` `[Authorize]`: (PATCH → PUT)
  - Обновить `DisplayName`, `Bio`, `WishlistPrivacy`
  - `POST /api/users/me/password` — смена пароля

- [x] `POST /api/users/me/avatar` `[Authorize]`:
  - Принять `multipart/form-data` с полем `file`
  - Валидировать: только JPEG/PNG/WebP, не более 5 МБ
  - Сохранить в `wwwroot/avatars/{userId}_{guid}.jpg` (уникальное имя)
  - Обновить `User.AvatarUrl`

- [x] `GET /api/users/search?q={query}`:
  - Поиск по `UserName`, `DisplayName`, `Email`
  - Вернуть список `UserProfileDto`, максимум 20

---

## Этап 8 — Друзья / подписки

### 8.1 DTOs (WishHub.Core/DTOs/Friends/)

- [x] `FriendshipDto.cs` — `Id`, `User: UserProfileDto`, `Status`, `CreatedAt`
- [x] `FriendRequestDto.cs` — `Id`, `From: UserProfileDto`, `CreatedAt`

### 8.2 FriendsController

- [x] `POST /api/friends/request/{userId}` `[Authorize]`:
  - Нельзя отправить самому себе
  - Проверить что дружба/заявка уже не существует
  - Создать `Friendship { Status = Pending, RequesterId = currentUserId, AddresseeId = userId }`
  - Создать `Notification` адресату типа `FriendRequest`

- [x] `POST /api/friends/accept/{friendshipId}` `[Authorize]`:
  - Проверить что `AddresseeId == currentUserId`
  - Установить `Status = Accepted`
  - Создать `Notification` инициатору типа `FriendRequestAccepted`

- [x] `DELETE /api/friends/request/{friendshipId}` `[Authorize]`:
  - Отклонить или отозвать (если `RequesterId == currentUserId` или `AddresseeId == currentUserId`)
  - Удалить запись

- [x] `DELETE /api/friends/{userId}` `[Authorize]`:
  - Удалить дружбу с userId (статус Accepted)

- [x] `GET /api/friends` `[Authorize]`:
  - Вернуть список принятых друзей (`Status == Accepted`)
  - Включить `UserProfileDto` другого участника

- [x] `GET /api/friends/requests` `[Authorize]`:
  - Query param `type=incoming|outgoing`
  - Входящие: `AddresseeId == currentUserId && Status == Pending`
  - Исходящие: `RequesterId == currentUserId && Status == Pending`

---

## Этап 9 — Сообщения и SignalR

### 9.1 DTOs (WishHub.Core/DTOs/Messages/)

- [x] `SendMessageRequest.cs` — `Content` (required, maxLength 2000)
- [x] `MessageDto.cs` — `Id`, `SenderId`, `ReceiverId`, `Content`, `IsRead`, `CreatedAt`
- [x] `DialogDto.cs` — `User: UserProfileDto`, `LastMessage: MessageDto`, `UnreadCount`

### 9.2 MessagesController

- [x] `POST /api/messages/{userId}` `[Authorize]`:
  - Проверить что пользователь существует
  - Создать `Message`
  - Отправить через SignalR в реальном времени
  - Создать `Notification` получателю типа `NewMessage`

- [x] `GET /api/messages/{userId}` `[Authorize]`:
  - История переписки, сортировка по `CreatedAt DESC`
  - Пагинация, `pageSize` = 20
  - `GET /api/messages/{messageId}/read` — пометить прочитанным

- [x] `GET /api/messages/dialogs` `[Authorize]`:
  - Получить уникальных собеседников
  - Для каждого: последнее сообщение + количество непрочитанных
  - `GET /api/messages/unread/count` — количество непрочитанных

### 9.3 SignalR Hub

- [x] `WishHub.Api/Hubs/ChatHub.cs` — SignalR хаб с методами:
  - `SendMessage(receiverId, content)` — сохранить и отправить в реальном времени
  - `MarkAsRead(messageId)` — пометить прочитанным
  - `JoinGroup` / `LeaveGroup` — групповые чаты
  - `OnConnectedAsync` / `OnDisconnectedAsync` — логирование

- [x] Зарегистрировано: `builder.Services.AddSignalR()` + `app.MapHub<ChatHub>("/hubs/chat")`

### 9.4 NotificationsController

- [x] `GET /api/notifications` `[Authorize]`:
  - Вернуть непрочитанные уведомления пользователя, последние 50
- [x] `POST /api/notifications/read-all` `[Authorize]`:
  - Пометить все как прочитанные
- [x] `POST /api/notifications/{id}/read` `[Authorize]`:
  - Пометить одно как прочитанное

---

## Этап 10 — Фоновые задачи (Hangfire)

### 10.1 Настройка Hangfire

- [x] Добавить в `Program.cs`:
```csharp
builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(connectionString));
builder.Services.AddHangfireServer();
app.UseHangfireDashboard("/hangfire", new DashboardOptions { ... });
```

- [x] Защитить `/hangfire` дашборд — `HangfireAuthorizationFilter`

### 10.2 Регистрация задач

- [x] `WishHub.Infrastructure/BackgroundJobs/PriceUpdateJob.cs`:
```csharp
public class PriceUpdateJob
{
    private readonly IProductService _productService;
    // ...
    public async Task ExecuteAsync()
        => await _productService.RefreshAllStaleProductsAsync();
}
```

- [x] В `Program.cs` зарегистрировать recurring job:
```csharp
RecurringJob.AddOrUpdate<PriceUpdateJob>(
    "price-update",
    x => x.ExecuteAsync(),
    "0 */8 * * *"); // Every 8 hours
```

---

## Этап 11 — OAuth: VK и Telegram

### 11.1 VK OAuth2

- [x] Конфигурация добавлена в `appsettings.json`:
```json
"OAuth": {
  "Vk": { "ClientId": "", "ClientSecret": "", "RedirectUri": "http://localhost:5000/api/auth/vk/callback" }
}
```
- [x] `GET /api/auth/vk` — редирект на VK OAuth с `state` защитой (cookie)
- [x] `GET /api/auth/vk/callback?code=...&state=...`:
  - Обмен `code` на `access_token` + `user_id` + `email`
  - Автоматическая привязка к существующему аккаунту по email
  - Создание нового пользователя если не найден
  - Сохранение `UserExternalLogin`

### 11.2 Telegram Login Widget

- [x] Конфигурация добавлена в `appsettings.json`:
```json
"OAuth": {
  "Telegram": { "BotToken": "", "BotUsername": "" }
}
```
- [x] `POST /api/auth/telegram` — принять данные от виджета:
  - `TelegramAuthRequest` record с полями: `Id`, `FirstName`, `LastName`, `Username`, `PhotoUrl`, `AuthDate`, `Hash`
  - Проверка подписи: `HMAC-SHA256(data_check_string, SHA256(BotToken))`
  - Автоматическая регистрация или вход
  - Поддержка аватара из Telegram

### 11.3 Привязка аккаунтов

- [x] `POST /api/auth/link/vk` `[Authorize]` — привязать VK (редирект на VK auth)
- [x] `POST /api/auth/link/telegram` `[Authorize]` — привязать Telegram
- [x] `DELETE /api/auth/unlink/{provider}` `[Authorize]`:
  - Проверка что остаётся хотя бы один способ входа (пароль или другой OAuth)
  - Удаление `UserExternalLogin`

---

## Этап 12 — Frontend

### 12.1 Настройка проекта

- [x] Установить зависимости:
```bash
npm install axios @tanstack/react-query zustand react-router-dom
npm install -D @types/node tailwindcss @tailwindcss/postcss
```
- [x] Настроить `vite.config.ts` — proxy `/api` → `http://localhost:5000`
- [x] Настроить `tsconfig.json` — strict mode, paths (`@/` → `src/`)
- [x] Настроить `tailwind.config.js` и `postcss.config.js`
- [x] Обновить `index.css` с Tailwind директивами
- [x] Создать `src/api/client.ts` — axios instance с базовым URL, интерцептор для JWT, интерцептор для refresh при 401

### 12.2 Типы (src/types/)

- [x] `auth.ts` — `User`, `AuthResponse`, `LoginRequest`, `RegisterRequest`
- [x] `wishlist.ts` — `WishlistItem`, `Product`, `AddWishlistItemRequest`
- [x] `friends.ts` — `Friendship`, `FriendRequest`
- [x] `messages.ts` — `Message`, `Dialog`
- [x] `notifications.ts` — `Notification`, `NotificationType`

### 12.3 Стейт (src/store/)

- [x] `auth.ts` — Zustand:
```ts
interface AuthStore {
  user: User | null;
  accessToken: string | null;
  setAuth: (user: User, token: string) => void;
  clearAuth: () => void;
}
```
- Персистировать `accessToken` в `localStorage`

- [x] `notifications.ts` — список непрочитанных, `unreadCount`

### 12.4 API-хуки (src/api/)

- [x] `useAuth.ts` — `useLogin`, `useRegister`, `useLogout`
- [x] `useWishlist.ts` — `useWishlist(userId)`, `useAddItem`, `useDeleteItem`, `useReserveItem`
- [x] `useProfile.ts` — `useProfile(username)`, `useUpdateProfile`, `useUploadAvatar`
- [x] `useFriends.ts` — `useFriends`, `useFriendRequests`, `useSendRequest`, `useAcceptRequest`
- [x] `useMessages.ts` — `useDialogs`, `useMessages(userId)`, `useSendMessage`
- [x] `useNotifications.ts` — `useNotifications`, `useMarkRead`

### 12.5 SignalR (src/hooks/)

- [x] `useSignalR.ts`:
  - Подключиться к `/hubs/chat?access_token=...`
  - Подписаться на `ReceiveMessage` — добавить в кэш TanStack Query
  - Подписаться на `ReceiveNotification` — обновить `notificationStore`
  - Автоматически переподключаться при обрыве

### 12.6 Страницы (src/pages/)

- [x] `LoginPage.tsx` — форма логина, ссылка на VK/Telegram login
- [x] `RegisterPage.tsx` — форма регистрации
- [x] `FeedPage.tsx` (`/`) — вишлисты друзей, последние добавления
- [x] `WishlistPage.tsx` (`/wishlist`) — свой вишлист:
  - Кнопка "Добавить по ссылке" → модалка
  - В модалке: input ссылки → кнопка "Загрузить" → превью карточки → кнопка "Добавить"
  - Список карточек с картинкой (через прокси), ценой, названием
  - Фильтры и сортировка
- [x] `ProfilePage.tsx` (`/profile/:username`) — профиль + вишлист пользователя:
  - Кнопка "Добавить в друзья" / "Отправлено" / "Друзья"
  - Кнопка "Написать сообщение"
- [x] `MessagesPage.tsx` (`/messages`) — список диалогов + чат
- [x] `FriendsPage.tsx` (`/friends`) — друзья + заявки
- [x] `SettingsPage.tsx` (`/settings`) — редактирование профиля, привязка OAuth

### 12.7 Компоненты (src/components/)

- [x] `ProductCard.tsx` — карточка товара (картинка, название, цена, кнопки)
- [x] `AddWishlistItemModal.tsx` — модалка добавления по ссылке
- [x] `UserAvatar.tsx` — аватар с fallback (инициалы)
- [x] `FriendshipButton.tsx` — кнопка с состоянием дружбы
- [x] `NotificationBell.tsx` — иконка с badge и дропдауном
- [x] `ChatWindow.tsx` — окно переписки с виртуальным скроллом
- [x] `PrivateRoute.tsx` — HOC для защищённых роутов

---

## Этап 13 — Тестирование

### 13.1 Тесты парсеров

- [x] `WishHub.Tests/Parsing/WildberriesParserTests.cs`:
  - Тест с реальной ссылкой (интеграционный, помечен `[Trait("Category", "Integration")]`)
  - Тест с мок-ответом HTTP (unit)
  - Тест что `CanParse` правильно определяет домен

- [x] Аналогично для `OzonParserTests.cs` и `YandexMarketParserTests.cs`

### 13.2 Тесты API (Integration)

- [x] `WishHub.Tests/Api/AuthControllerTests.cs`:
  - Регистрация → логин → получение токена → обращение к защищённому эндпоинту
  - Refresh токен
  - Логаут → refresh возвращает 401

- [x] `WishHub.Tests/Api/WishlistControllerTests.cs`:
  - Добавить товар (мокнуть `IProductService`)
  - Получить вишлист
  - Проверить приватность (FriendsOnly — незнакомец получает 403)

- [ ] Использовать `WebApplicationFactory<Program>` + `Testcontainers.PostgreSql`

---

## Этап 14 — Деплой

### 14.1 Dockerfile

- [x] `WishHub.Api/Dockerfile` (multi-stage):
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish WishHub.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Playwright
RUN apt-get update && apt-get install -y chromium
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN dotnet tool install --global Microsoft.Playwright.CLI
RUN playwright install chromium
ENTRYPOINT ["dotnet", "WishHub.Api.dll"]
```

### 14.2 docker-compose.prod.yml

- [x] Добавить сервис `api` (билд из Dockerfile)
- [x] Добавить `nginx` как reverse proxy
- [x] Добавить сервис `frontend` (билд из `frontend/Dockerfile`)
- [x] Все секреты через `.env` файл (не в git)
- [x] `volumes` для `wwwroot/avatars/`
- [x] `docker-compose.registry.yml` для EasyPanel/GHCR с дефолтным `IMAGE_OWNER`

### 14.3 Миграции при старте

- [x] В `Program.cs` добавить:
```csharp
using var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
await db.Database.MigrateAsync();
```

---

## Чеклист ключевых решений

- [ ] Все `DateTime` хранятся как `UTC` (Npgsql: `timestamp with time zone`)
- [ ] Пагинация cursor-based (не offset) для вишлиста и сообщений
- [ ] `ReservedBy` скрыт от владельца вишлиста (не включать в DTO)
- [ ] `ImageProxyUrl` формируется на уровне маппинга DTO, не хранится в БД
- [ ] SignalR токен из query string (стандартный подход для WebSocket)
- [ ] Validation через `[ApiController]` + DataAnnotations (или FluentValidation)
- [ ] Все внешние HTTP-запросы (парсинг, OAuth) обёрнуты в `try/catch` + `ILogger`
- [ ] Hangfire Dashboard доступен только в Development или за авторизацией
