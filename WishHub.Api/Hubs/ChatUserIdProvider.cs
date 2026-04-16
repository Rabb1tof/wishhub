using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace WishHub.Api.Hubs;

/// <summary>
/// Явно извлекает userId из JWT-клейма для корректного маршрутизирования SignalR-событий через Clients.User(userId).
/// JWT сериализует ClaimTypes.NameIdentifier как "sub", а дефолтный провайдер может не найти его.
/// </summary>
public class ChatUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        // Сначала пробуем ClaimTypes.NameIdentifier (мапится из "sub")
        var claim = connection.User?.FindFirst(ClaimTypes.NameIdentifier);
        if (claim != null) return claim.Value;

        // Фоллбэк: пробуем "sub" напрямую
        claim = connection.User?.FindFirst("sub");
        return claim?.Value;
    }
}
