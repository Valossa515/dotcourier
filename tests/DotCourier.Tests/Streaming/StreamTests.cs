using System.Diagnostics;
using DotCourier.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotCourier.Tests.Streaming;

public class StreamTests
{
    private static async Task<List<T>> ReadAll<T>(IAsyncEnumerable<T> stream, CancellationToken cancellationToken = default)
    {
        var items = new List<T>();
        await foreach (var item in stream.WithCancellation(cancellationToken))
        {
            items.Add(item);
        }

        return items;
    }

    [Fact]
    public async Task CreateStream_returns_the_items_in_order()
    {
        var (services, log) = TestHost.Build();

        var items = await ReadAll(services.Courier().CreateStream(new CountTo(3)));

        Assert.Equal([1, 2, 3], items);
        Assert.Equal(["produced 1", "produced 2", "produced 3", "handler finished"], log.Entries);
    }

    [Fact]
    public async Task Nothing_runs_until_the_stream_is_read()
    {
        var (services, log) = TestHost.Build();

        var stream = services.Courier().CreateStream(new CountTo(3));
        var missing = services.Courier().CreateStream(new Unhandled()); // no exception yet, like MediatR

        Assert.Empty(log.Entries);
        await Assert.ThrowsAsync<HandlerNotFoundException>(() => ReadAll(missing));
        Assert.Equal([1, 2, 3], await ReadAll(stream));
    }

    [Fact]
    public async Task An_invalid_request_is_rejected_before_the_handler_runs()
    {
        var (services, log) = TestHost.Build();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => ReadAll(services.Courier().CreateStream(new CountTo(0))));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains(ex.Errors, e => e.Field == nameof(CountTo.Count));
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Stream_behaviors_wrap_the_stream_in_order()
    {
        var (services, log) = TestHost.Build(cfg => cfg
            .AddOpenStreamBehavior(typeof(TracingStreamBehavior<,>), order: -1)
            .AddStreamBehavior<IStreamPipelineBehavior<CountTo, int>, DoublingBehavior>());

        var items = await ReadAll(services.Courier().CreateStream(new CountTo(2)));

        Assert.Equal([2, 4], items);
        Assert.Equal(["before stream", "produced 1", "produced 2", "handler finished", "after stream"], log.Entries);
    }

    [Fact]
    public async Task The_caller_token_cancels_the_stream()
    {
        var (services, _) = TestHost.Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadAll(services.Courier().CreateStream(new Ticks(), cts.Token)));
    }

    [Fact]
    public async Task WithCancellation_cancels_the_stream_too()
    {
        var (services, _) = TestHost.Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadAll(services.Courier().CreateStream(new Ticks()), cts.Token));
    }

    [Fact]
    public async Task Stopping_early_ends_the_handler_and_is_logged()
    {
        var logs = new CapturingLoggerProvider();
        var (services, log) = TestHost.Build(logs: logs);

        await foreach (var item in services.Courier().CreateStream(new CountTo(10)))
        {
            if (item == 3)
            {
                break;
            }
        }

        Assert.Equal(["produced 1", "produced 2", "produced 3", "handler finished"], log.Entries);
        Assert.Contains(logs.Entries, e => e.Message.StartsWith("[Courier] <-- CountTo stream stopped by the caller after 3 item(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failure_mid_stream_reaches_the_caller_after_the_items_before_it()
    {
        var logs = new CapturingLoggerProvider();
        var (services, _) = TestHost.Build(logs: logs);
        var received = new List<int>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in services.Courier().CreateStream(new CountTo(5, FailAt: 3)))
            {
                received.Add(item);
            }
        });

        Assert.Equal("failed at 3", ex.Message);
        Assert.Equal([1, 2], received);
        var error = Assert.Single(logs.Entries, e => e.Level == LogLevel.Error);
        Assert.StartsWith("[Courier] <-- CountTo stream threw InvalidOperationException after 2 item(s)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_and_completion_are_logged_without_the_items()
    {
        var logs = new CapturingLoggerProvider();
        var (services, _) = TestHost.Build(logs: logs);

        await ReadAll(services.Courier().CreateStream(new CountTo(5)));

        var messages = logs.Entries.Where(e => e.Category.Contains(nameof(StreamLoggingBehavior<CountTo, int>), StringComparison.Ordinal)).Select(e => e.Message).ToArray();
        Assert.Equal(2, messages.Length);
        Assert.Equal("[Courier] --> Streaming CountTo", messages[0]);
        Assert.StartsWith("[Courier] <-- CountTo streamed 5 item(s) in ", messages[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_activity_spans_the_stream_and_parents_the_handler_spans()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CourierDiagnostics.SourceName || source.Name == CountToHandler.Source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (activities)
                {
                    activities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        var (services, _) = TestHost.Build();

        await ReadAll(services.Courier().CreateStream(new CountTo(3)));

        Activity stream;
        Activity[] reads;
        lock (activities)
        {
            stream = Assert.Single(activities, a => a.DisplayName == "courier CountTo");
            reads = activities.Where(a => a.DisplayName.StartsWith("read ", StringComparison.Ordinal) && a.TraceId == stream.TraceId).ToArray();
        }

        Assert.Equal(3, stream.GetTagItem("courier.stream.items"));
        Assert.Equal(3, reads.Length);
        Assert.All(reads, r => Assert.Equal(stream.SpanId, r.ParentSpanId)); // also after the first yield
    }

    [Fact]
    public async Task Open_generic_stream_handlers_are_closed_on_first_use()
    {
        var (services, _) = TestHost.Build();

        Assert.Equal(["a", "b"], await ReadAll(services.Courier().CreateStream(new StreamAll<string>(["a", "b"]))));
        Assert.Equal([4, 2], await ReadAll(services.Courier().CreateStream(new StreamAll<int>([4, 2]))));
    }

    [Fact]
    public async Task Untyped_streams_work_like_Send_object()
    {
        var (services, _) = TestHost.Build();
        IMediator mediator = services.GetRequiredService<IMediator>();

        Assert.Equal([1, 2], await ReadAll(mediator.CreateStream((object)new CountTo(2))));
        Assert.Throws<ArgumentException>(() => mediator.CreateStream(new object()));
    }

    [Fact]
    public async Task Built_in_stream_behaviors_follow_the_same_switches()
    {
        var logs = new CapturingLoggerProvider();
        var (services, _) = TestHost.Build(cfg =>
        {
            cfg.EnableLogging = false;
            cfg.EnableValidation = false;
        }, logs);

        var items = await ReadAll(services.Courier().CreateStream(new CountTo(0))); // invalid, but validation is off

        Assert.Empty(items);
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains("Streaming", StringComparison.Ordinal));
    }
}
