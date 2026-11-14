using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using WishHub.Api.Hubs;
using WishHub.Core.DTOs.Messages;
using WishHub.Core.Interfaces;

namespace WishHub.Tests.Api;

public class ChatHubSurfaceTests
{
    [Theory]
    [InlineData("MarkAsRead")]
    [InlineData("JoinGroup")]
    [InlineData("LeaveGroup")]
    public async Task Invoke_RemovedMethod_ReturnsUnknownMethodWithoutServiceCalls(string method)
    {
        var messages = Substitute.For<IMessageService>();
        using var server = CreateServer(messages);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var socket = await ConnectAsync(server, Guid.NewGuid(), timeout.Token);
        // A GUID is valid for the former read method and also valid as a group name.
        await SendAsync(socket, new
        {
            type = 1, invocationId = "removed", target = method,
            arguments = new[] { Guid.NewGuid().ToString() }
        }, timeout.Token);

        var frames = await ReceiveUntilAsync(socket, IsCompletion, timeout.Token);
        var completion = Assert.Single(frames, IsCompletion);
        Assert.Equal("removed", completion.GetProperty("invocationId").GetString());
        Assert.True(completion.TryGetProperty("error", out var error), $"Expected an unknown-method error, got {completion}.");
        Assert.Equal($"Failed to invoke '{method}' due to an error on the server. HubException: Method does not exist.", error.GetString());
        Assert.Empty(messages.ReceivedCalls());
    }

    [Fact]
    public async Task SendMessage_AuthenticatedCaller_DelegatesAndDeliversToSenderAndReceiver()
    {
        var senderId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();
        var message = new MessageDto
        {
            Id = Guid.NewGuid(), SenderId = senderId, ReceiverId = receiverId,
            Content = "A gift for Saturday", CreatedAt = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc)
        };
        var messages = Substitute.For<IMessageService>();
        messages.SendMessageAsync(senderId, receiverId, message.Content, Arg.Any<CancellationToken>()).Returns(message);
        using var server = CreateServer(messages);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var receiver = await ConnectAsync(server, receiverId, timeout.Token);
        using var sender = await ConnectAsync(server, senderId, timeout.Token);
        using var observer = await ConnectAsync(server, Guid.NewGuid(), timeout.Token);

        await SendAsync(sender, new
        {
            type = 1, invocationId = "send", target = "SendMessage",
            arguments = new object[] { receiverId, message.Content }
        }, timeout.Token);

        var senderFrames = await ReceiveUntilAsync(sender, IsCompletion, timeout.Token);
        var completion = Assert.Single(senderFrames, IsCompletion);
        Assert.Equal("send", completion.GetProperty("invocationId").GetString());
        Assert.False(completion.TryGetProperty("error", out _));
        var confirmation = Assert.Single(senderFrames, frame => IsEvent(frame, "MessageSent"));
        AssertMessage(confirmation.GetProperty("arguments")[0], message);
        Assert.DoesNotContain(senderFrames, frame => IsEvent(frame, "ReceiveMessage"));

        // SendMessage has completed all writes; this response drains any earlier observer deliveries.
        await SendAsync(observer, new
        {
            type = 1, invocationId = "barrier", target = "DeliveryBarrier", arguments = Array.Empty<object>()
        }, timeout.Token);
        var observerFrames = await ReceiveUntilAsync(observer, IsCompletion, timeout.Token);
        var barrier = Assert.Single(observerFrames, IsCompletion);
        Assert.Equal("barrier", barrier.GetProperty("invocationId").GetString());
        Assert.Equal("Failed to invoke 'DeliveryBarrier' due to an error on the server. HubException: Method does not exist.",
            barrier.GetProperty("error").GetString());
        Assert.DoesNotContain(observerFrames, frame => frame.GetProperty("type").GetInt32() == 1);

        var receiverFrames = await ReceiveUntilAsync(receiver, frame => IsEvent(frame, "ReceiveMessage"), timeout.Token);
        var delivery = Assert.Single(receiverFrames, frame => IsEvent(frame, "ReceiveMessage"));
        AssertMessage(delivery.GetProperty("arguments")[0], message);
        await messages.Received(1).SendMessageAsync(senderId, receiverId, message.Content, Arg.Any<CancellationToken>());
        Assert.Single(messages.ReceivedCalls());
    }

    private static TestServer CreateServer(IMessageService messages) => new(new WebHostBuilder()
        .ConfigureServices(services =>
        {
            services.AddSingleton(messages);
            services.AddSignalR();
            services.AddAuthorization();
        })
        .Configure(app =>
        {
            app.UseRouting();
            app.Use((context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, context.Request.Headers["X-Test-User"].ToString())], "Test"));
                return next(context);
            });
            app.UseAuthorization();
            app.UseEndpoints(endpoints => endpoints.MapHub<ChatHub>("/hubs/chat"));
        }));

    private static async Task<WebSocket> ConnectAsync(TestServer server, Guid userId, CancellationToken ct)
    {
        var client = server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["X-Test-User"] = userId.ToString();
        var socket = await client.ConnectAsync(new Uri("ws://localhost/hubs/chat"), ct);
        await SendAsync(socket, new { protocol = "json", version = 1 }, ct);
        Assert.Equal("{}\u001e", await ReceiveTextAsync(socket, ct));
        return socket;
    }

    private static Task SendAsync(WebSocket socket, object payload, CancellationToken ct) =>
        socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload) + '\u001e')),
            WebSocketMessageType.Text, true, ct);

    private static async Task<List<JsonElement>> ReceiveUntilAsync(WebSocket socket, Predicate<JsonElement> match, CancellationToken ct)
    {
        var frames = new List<JsonElement>();
        do
        {
            var text = await ReceiveTextAsync(socket, ct);
            foreach (var frame in text.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
            {
                using var json = JsonDocument.Parse(frame);
                frames.Add(json.RootElement.Clone());
            }
        } while (!frames.Exists(match));
        return frames;
    }

    private static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken ct)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            bytes.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static bool IsCompletion(JsonElement frame) => frame.GetProperty("type").GetInt32() == 3;
    private static bool IsEvent(JsonElement frame, string target) =>
        frame.GetProperty("type").GetInt32() == 1 && frame.GetProperty("target").GetString() == target;

    private static void AssertMessage(JsonElement actual, MessageDto expected)
    {
        Assert.Equal(expected.Id, actual.GetProperty("id").GetGuid());
        Assert.Equal(expected.SenderId, actual.GetProperty("senderId").GetGuid());
        Assert.Equal(expected.ReceiverId, actual.GetProperty("receiverId").GetGuid());
        Assert.Equal(expected.Content, actual.GetProperty("content").GetString());
        Assert.Equal(expected.CreatedAt, actual.GetProperty("createdAt").GetDateTime());
    }
}
