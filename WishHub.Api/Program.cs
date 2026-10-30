using Microsoft.EntityFrameworkCore;
using WishHub.Api;
using WishHub.Api.Extensions;
using WishHub.Api.Hubs;
using WishHub.Api.Middleware;
using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;

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
        builder.Services.AddScoped<IWishlistService, WishlistService>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<IFriendshipService, FriendshipService>();
        builder.Services.AddScoped<IMessageService, MessageService>();
        builder.Services.AddScoped<INotificationService, NotificationService>();

        builder.Services.AddProductMetadata(builder.Configuration);

        // Add SignalR
        builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, ChatUserIdProvider>();
        builder.Services.AddSignalR();

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

        // Health checks (used by docker HEALTHCHECK and EasyPanel)
        builder.Services.AddHealthChecks();

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
        app.UseRateLimiter();

        app.MapControllers();
        app.MapHub<ChatHub>("/hubs/chat");
        app.MapHealthChecks("/health");

        // Run database migrations at startup
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();

        app.Run();
    }
}
