using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WishHub.Api.Controllers;
using WishHub.Core.Interfaces;
using WishHub.Core.Validation;

namespace WishHub.Tests.Api;

public sealed class UsersAvatarControllerTests
{
    [Fact]
    public async Task Upload_OversizedFile_Returns413WithoutOpeningStreamOrCallingService()
    {
        var service = new Mock<IUserService>(MockBehavior.Strict);
        var file = new Mock<IFormFile>(MockBehavior.Strict);
        file.SetupGet(value => value.Length).Returns(WishlistImageLimits.MaxFileBytes + 1);
        var controller = Controller(service.Object, Guid.NewGuid());
        var response = await controller.UploadAvatar(file.Object);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, Assert.IsType<ObjectResult>(response.Result).StatusCode);
        file.Verify(value => value.OpenReadStream(), Times.Never);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Upload_PassesCancellationAndDisposesStreamAfterServiceFailure()
    {
        var owner = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var content = new ObservedStream();
        var file = new Mock<IFormFile>();
        file.SetupGet(value => value.Length).Returns(12);
        file.SetupGet(value => value.ContentType).Returns("image/png");
        file.SetupGet(value => value.FileName).Returns("avatar.png");
        file.Setup(value => value.OpenReadStream()).Returns(content);
        var service = new Mock<IUserService>(MockBehavior.Strict);
        service.Setup(value => value.UploadAvatarAsync(owner, content, "image/png", "avatar.png", cancellation.Token))
            .Callback(() => Assert.False(content.Disposed)).ThrowsAsync(new IOException("Synthetic save failure."));
        var controller = Controller(service.Object, owner);
        await Assert.ThrowsAsync<IOException>(() => controller.UploadAvatar(file.Object, cancellation.Token));
        Assert.True(content.Disposed);
        service.VerifyAll();
    }

    private static UsersController Controller(IUserService service, Guid owner) => new(service, NullLogger<UsersController>.Instance)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner.ToString())], "test"))
            }
        }
    };

    private sealed class ObservedStream : MemoryStream
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
