using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WishHub.Parsing.Ozon;

namespace WishHub.Tests.Parsing.Ozon;

public class RequestSchedulerTests
{
    [Fact]
    public async Task ExecuteAsync_AppliesStartJitterBeforeRunningWork()
    {
        var random = new SequenceRandomValueProvider(3456);
        var delay = new RecordingDelay();
        var scheduler = CreateScheduler(random, delay);
        var executed = false;

        var result = await scheduler.ExecuteAsync<int>(_ =>
        {
            executed = true;
            return Task.FromResult(42);
        });

        result.Should().Be(42);
        executed.Should().BeTrue();
        delay.Recorded.Should().Equal(3456);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRunSameProxyConcurrently()
    {
        var random = new SequenceRandomValueProvider(3000, 3000);
        var delay = new GateDelay();
        var scheduler = CreateScheduler(random, delay);
        var order = new List<string>();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = scheduler.ExecuteAsync(async _ =>
        {
            order.Add("first-start");
            firstStarted.SetResult();
            await releaseFirstWork.Task;
            order.Add("first-end");
            return 1;
        }, proxyUrl: "http://proxy-1:8080");

        await delay.WaitForCallsAsync(1);
        delay.ReleaseNext();
        await firstStarted.Task;

        var secondReachedWork = false;
        var second = scheduler.ExecuteAsync(_ =>
        {
            secondReachedWork = true;
            order.Add("second-start");
            return Task.FromResult(2);
        }, proxyUrl: "http://proxy-1:8080");

        await Task.Delay(50);
        secondReachedWork.Should().BeFalse();

        releaseFirstWork.SetResult();
        await first;
        await delay.WaitForCallsAsync(2);
        delay.ReleaseNext();
        await second;

        order.Should().Equal("first-start", "first-end", "second-start");
    }

    [Fact]
    public async Task ExecuteAsync_AllowsDifferentProxiesInParallelUpToLimit()
    {
        var random = new SequenceRandomValueProvider(3000, 3000);
        var delay = new GateDelay();
        var scheduler = CreateScheduler(random, delay, maxParallelContexts: 2);
        var runningCount = 0;
        var peakRunningCount = 0;
        var releaseWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int> CreateTask(string proxyUrl) => scheduler.ExecuteAsync(async _ =>
        {
            var current = Interlocked.Increment(ref runningCount);
            peakRunningCount = Math.Max(peakRunningCount, current);
            await releaseWork.Task;
            Interlocked.Decrement(ref runningCount);
            return current;
        }, proxyUrl);

        var first = CreateTask("http://proxy-1:8080");
        var second = CreateTask("http://proxy-2:8080");

        await delay.WaitForCallsAsync(2);
        delay.ReleaseNext();
        delay.ReleaseNext();
        await Task.Delay(50);

        peakRunningCount.Should().Be(2);

        releaseWork.SetResult();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsGlobalParallelismLimit()
    {
        var random = new SequenceRandomValueProvider(3000, 3000, 3000);
        var delay = new GateDelay();
        var scheduler = CreateScheduler(random, delay, maxParallelContexts: 2);
        var started = 0;
        var releaseWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int> CreateTask(string proxyUrl) => scheduler.ExecuteAsync(async _ =>
        {
            Interlocked.Increment(ref started);
            await releaseWork.Task;
            return 1;
        }, proxyUrl);

        var first = CreateTask("http://proxy-1:8080");
        var second = CreateTask("http://proxy-2:8080");
        var third = CreateTask("http://proxy-3:8080");

        await delay.WaitForCallsAsync(2);
        delay.ReleaseNext();
        delay.ReleaseNext();
        await Task.Delay(50);
        started.Should().Be(2);

        releaseWork.SetResult();
        await delay.WaitForCallsAsync(3);
        delay.ReleaseNext();
        await Task.WhenAll(first, second, third);
    }

    private static RequestScheduler CreateScheduler(
        IRandomValueProvider random,
        IAsyncDelay delay,
        int maxParallelContexts = 3)
    {
        return new RequestScheduler(
            Options.Create(new RequestSchedulerOptions
            {
                MaxParallelContexts = maxParallelContexts,
                MinStartDelayMs = 3000,
                MaxStartDelayMs = 12000
            }),
            random,
            delay,
            NullLogger<RequestScheduler>.Instance);
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

    private sealed class GateDelay : IAsyncDelay
    {
        private readonly Queue<TaskCompletionSource> _gates = new();
        private readonly object _sync = new();
        private TaskCompletionSource? _waiter;
        private int _callCount;

        public Task DelayAsync(int millisecondsDelay, CancellationToken ct = default)
        {
            TaskCompletionSource gate;
            TaskCompletionSource? waiterToRelease = null;

            lock (_sync)
            {
                gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _gates.Enqueue(gate);
                _callCount++;
                waiterToRelease = _waiter;
                _waiter = null;
            }

            waiterToRelease?.SetResult();
            return gate.Task.WaitAsync(ct);
        }

        public async Task WaitForCallsAsync(int expectedCallCount)
        {
            while (true)
            {
                TaskCompletionSource waiter;
                lock (_sync)
                {
                    if (_callCount >= expectedCallCount)
                    {
                        return;
                    }

                    _waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    waiter = _waiter;
                }

                await waiter.Task;
            }
        }

        public void ReleaseNext()
        {
            TaskCompletionSource gate;
            lock (_sync)
            {
                gate = _gates.Dequeue();
            }

            gate.SetResult();
        }
    }

    private sealed class SequenceRandomValueProvider : IRandomValueProvider
    {
        private readonly Queue<int> _values;

        public SequenceRandomValueProvider(params int[] values)
        {
            _values = new Queue<int>(values);
        }

        public int Next(int minValue, int maxValueExclusive)
        {
            if (_values.Count == 0)
            {
                return minValue;
            }

            return _values.Dequeue();
        }
    }
}
