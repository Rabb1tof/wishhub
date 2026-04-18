using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using WishHub.Api;
using WishHub.Api.Extensions;
using WishHub.Api.Hubs;
using WishHub.Api.Middleware;
using WishHub.Core.Interfaces;
using WishHub.Core.Services;
using WishHub.Infrastructure.BackgroundJobs;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;
using WishHub.Parsing.Ozon;
using WishHub.Parsing.Parsers;
using WishHub.Parsing.Playwright.Browsers;
using WishHub.Parsing.Playwright.Contexts;
using WishHub.Parsing.Playwright.Fingerprinting;
using WishHub.Parsing.Playwright.Stealth;

namespace WishHub.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container
        builder.Services.AddIdentityServices(builder.Configuration);

        // Register custom services
        builder.Services.AddScoped<ITokenService, TokenService>();
        builder.Services.AddScoped<IProductService, ProductService>();
        builder.Services.AddScoped<IWishlistService, WishlistService>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<IFriendshipService, FriendshipService>();
        builder.Services.AddScoped<IMessageService, MessageService>();
        builder.Services.AddScoped<INotificationService, NotificationService>();
        builder.Services.AddScoped<IBackgroundParsingService, BackgroundParsingService>();
        builder.Services.AddScoped<PriceUpdateJob>();

        // Add Hangfire
        builder.Services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(builder.Configuration.GetConnectionString("Default")));

        builder.Services.AddHangfireServer();

        // Add SignalR
        builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, ChatUserIdProvider>();
        builder.Services.AddSignalR();

        // Playwright browser provider (safe fallback to null if not installed)
        builder.Services.AddSingleton<BrowserProvider>();

        builder.Services.Configure<SessionManagerOptions>(builder.Configuration.GetSection("Parsing:Ozon:Sessions"));
        builder.Services.Configure<RequestSchedulerOptions>(builder.Configuration.GetSection("Parsing:Ozon:Scheduler"));

        builder.Services.AddSingleton<IFingerprintPool, FingerprintPool>();
        builder.Services.AddSingleton<IStealthInitScriptFactory, StealthInitScriptFactory>();
        builder.Services.AddSingleton<IChromiumBrowserLauncher, PlaywrightChromiumBrowserLauncher>();
        builder.Services.AddSingleton<IBrowserContextFactory, BrowserContextFactory>();
        builder.Services.AddSingleton<IRandomValueProvider, RandomValueProvider>();
        builder.Services.AddSingleton<IAsyncDelay, AsyncDelay>();
        builder.Services.AddSingleton<ISessionManager, SessionManager>();
        builder.Services.AddSingleton<IHumanBehaviorSimulator, HumanBehaviorSimulator>();
        builder.Services.AddSingleton<IBlockDetector, BlockDetector>();
        builder.Services.AddSingleton<IProxyProvider, NoOpProxyProvider>();
        builder.Services.AddSingleton<IRequestScheduler, RequestScheduler>();
        builder.Services.AddSingleton<IOzonProductScraper, OzonProductScraper>();

        // Register parsers (after Playwright so browser is available)
        builder.Services.AddSingleton<ParserFactory>();
        builder.Services.AddHttpClient<WildberriesParser>()
            .AddTypedClient((httpClient, sp) => new WildberriesParser(httpClient, sp.GetRequiredService<BrowserProvider>().Browser));

        builder.Services.AddHttpClient<OzonParser>()
            .AddTypedClient((httpClient, sp) => new OzonParser(
                httpClient,
                sp.GetRequiredService<IOzonProductScraper>(),
                sp.GetRequiredService<IFingerprintPool>(),
                sp.GetRequiredService<ILogger<OzonParser>>()));

        builder.Services.AddHttpClient<YandexMarketParser>()
            .AddTypedClient((httpClient, sp) => new YandexMarketParser(httpClient, sp.GetRequiredService<BrowserProvider>().Browser));

        builder.Services.AddSingleton<IProductParser>(sp => sp.GetRequiredService<WildberriesParser>());
        builder.Services.AddSingleton<IProductParser>(sp => sp.GetRequiredService<OzonParser>());
        builder.Services.AddSingleton<IProductParser>(sp => sp.GetRequiredService<YandexMarketParser>());

        // Register Redis
        builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

        // Register ImageProxy HttpClient with headers
        builder.Services.AddHttpClient("ImageProxy", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        });

        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy =
                    System.Text.Json.JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.Converters.Add(
                    new System.Text.Json.Serialization.JsonStringEnumConverter(
                        System.Text.Json.JsonNamingPolicy.CamelCase));
            });
        builder.Services.AddEndpointsApiExplorer();

        // CORS
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            });
        });

        // Add Swagger with JWT support
        builder.Services.AddSwaggerGen(c =>
        {
            c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
                Name = "Authorization",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });
            c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
            {
                {
                    new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        Reference = new Microsoft.OpenApi.Models.OpenApiReference
                        {
                            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        var app = builder.Build();

        // Configure the HTTP request pipeline
        // CORS должен быть ПЕРВЫМ — обрабатывает preflight до других middleware
        app.UseCors("AllowFrontend");

        app.UseMiddleware<ExceptionHandlingMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();

        // Serve static files (avatars)
        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHub<ChatHub>("/hubs/chat");

        // Hangfire Dashboard (basic auth recommended for production)
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = new[] { new HangfireAuthorizationFilter() }
        });

        // Запускаем проверку ежечасно; сам job решает какие продукты обновлять
        // на основе индивидуальных настроек пользователей (PriceRefreshIntervalHours)
        RecurringJob.AddOrUpdate<PriceUpdateJob>(
            "price-update",
            x => x.ExecuteAsync(),
            Cron.Hourly());

        // Run database migrations at startup
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();

        app.Run();
    }
}
