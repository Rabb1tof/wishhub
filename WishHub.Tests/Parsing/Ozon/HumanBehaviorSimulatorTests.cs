using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using WishHub.Parsing.Ozon;

namespace WishHub.Tests.Parsing.Ozon;

public class HumanBehaviorSimulatorTests
{
    [Fact]
    public async Task RandomDelayAsync_UsesRandomDelayWithinRange()
    {
        var random = new Mock<IRandomValueProvider>();
        var delay = new RecordingDelay();
        random.Setup(x => x.Next(100, 201)).Returns(157);

        var simulator = new HumanBehaviorSimulator(random.Object, delay, NullLogger<HumanBehaviorSimulator>.Instance);

        await simulator.RandomDelayAsync(100, 200);

        delay.Recorded.Should().ContainSingle().Which.Should().Be(157);
    }

    [Fact]
    public async Task SimulateReadingPauseAsync_UsesThreeToEightSecondRange()
    {
        var random = new Mock<IRandomValueProvider>();
        var delay = new RecordingDelay();
        random.Setup(x => x.Next(3000, 8001)).Returns(6123);

        var simulator = new HumanBehaviorSimulator(random.Object, delay, NullLogger<HumanBehaviorSimulator>.Instance);

        await simulator.SimulateReadingPauseAsync();

        delay.Recorded.Should().ContainSingle().Which.Should().Be(6123);
    }

    [Fact]
    public async Task SimulateScrollAsync_UsesChunkedWheelScrollingWithJitter()
    {
        var random = new Mock<IRandomValueProvider>();
        var delay = new RecordingDelay();
        var mouse = new Mock<IMouse>();
        var page = new Mock<IPage>();
        var wheelDeltas = new List<float>();

        page.SetupGet(x => x.Mouse).Returns(mouse.Object);
        mouse
            .Setup(x => x.WheelAsync(0, It.IsAny<float>()))
            .Callback<float, float>((_, deltaY) => wheelDeltas.Add(deltaY))
            .Returns(Task.CompletedTask);

        random.SetupSequence(x => x.Next(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(350)
            .Returns(120)
            .Returns(180)
            .Returns(120)
            .Returns(170)
            .Returns(150)
            .Returns(130);

        var simulator = new HumanBehaviorSimulator(random.Object, delay, NullLogger<HumanBehaviorSimulator>.Instance);

        await simulator.SimulateScrollAsync(page.Object, 300, 400);

        wheelDeltas.Should().HaveCount(3);
        wheelDeltas.Should().OnlyContain(value => value >= 50 && value <= 200);
        wheelDeltas.Sum().Should().Be(350);
        delay.Recorded.Should().Equal(180, 170, 130);
    }

    [Fact]
    public async Task SimulateMouseMovementAsync_UsesCurvedMultiStepMoves()
    {
        var random = new Mock<IRandomValueProvider>();
        var delay = new RecordingDelay();
        var mouse = new Mock<IMouse>();
        var page = new Mock<IPage>();
        var recordedMoves = new List<(float X, float Y, int? Steps)>();

        page.SetupGet(x => x.Mouse).Returns(mouse.Object);
        mouse
            .Setup(x => x.MoveAsync(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<MouseMoveOptions?>()))
            .Callback<float, float, MouseMoveOptions?>((x, y, options) => recordedMoves.Add((x, y, options?.Steps)))
            .Returns(Task.CompletedTask);

        random.SetupSequence(x => x.Next(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(3)
            .Returns(100)
            .Returns(120)
            .Returns(5)
            .Returns(500)
            .Returns(400)
            .Returns(60)
            .Returns(-20)
            .Returns(9)
            .Returns(11)
            .Returns(13)
            .Returns(80)
            .Returns(720)
            .Returns(540)
            .Returns(-45)
            .Returns(50)
            .Returns(10)
            .Returns(12)
            .Returns(14)
            .Returns(90)
            .Returns(900)
            .Returns(600)
            .Returns(30)
            .Returns(-70)
            .Returns(15)
            .Returns(16)
            .Returns(17)
            .Returns(120);

        var simulator = new HumanBehaviorSimulator(random.Object, delay, NullLogger<HumanBehaviorSimulator>.Instance);

        await simulator.SimulateMouseMovementAsync(page.Object);

        recordedMoves.Should().HaveCount(10);
        recordedMoves.First().Should().Be((100f, 120f, 5));
        recordedMoves.Skip(1).Should().OnlyContain(move => move.Steps >= 8 && move.Steps <= 17);
        recordedMoves.Select(move => (move.X, move.Y)).Distinct().Count().Should().BeGreaterThan(4);
        delay.Recorded.Should().Equal(80, 90, 120);
    }

    private sealed class RecordingDelay : IAsyncDelay
    {
        public List<int> Recorded { get; } = [];

        public Task DelayAsync(int millisecondsDelay, CancellationToken ct = default)
        {
            Recorded.Add(millisecondsDelay);
            return Task.CompletedTask;
        }
    }
}
