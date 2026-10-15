using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace DotCourier.Tests.Streaming;

/// <summary>Streams 1..<see cref="Count"/>; DataAnnotations reject counts outside 1..100.</summary>
public sealed record CountTo([property: Range(1, 100)] int Count, int FailAt = 0) : IStreamRequest<int>;

public sealed class CountToHandler(CallLog log) : IStreamRequestHandler<CountTo, int>
{
    public static readonly ActivitySource Source = new("DotCourier.Tests.Streaming");

    public async IAsyncEnumerable<int> Handle(CountTo request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            for (var i = 1; i <= request.Count; i++)
            {
                await Task.Yield();
                if (i == request.FailAt)
                {
                    throw new InvalidOperationException($"failed at {i}");
                }

                // A span per item, like a database read: it must be a child of the stream's span.
                using (Source.StartActivity("read " + i))
                {
                    log.Add("produced " + i);
                }

                yield return i;
            }
        }
        finally
        {
            log.Add("handler finished");
        }
    }
}

/// <summary>Never ends on its own: only cancellation stops it.</summary>
public sealed record Ticks : IStreamRequest<int>;

public sealed class TicksHandler : IStreamRequestHandler<Ticks, int>
{
    public async IAsyncEnumerable<int> Handle(Ticks request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; ; i++)
        {
            await Task.Delay(5, cancellationToken);
            yield return i;
        }
    }
}

/// <summary>A generic stream request served by one open generic handler.</summary>
public sealed record StreamAll<T>(IReadOnlyList<T> Items) : IStreamRequest<T>;

public sealed class StreamAllHandler<T> : IStreamRequestHandler<StreamAll<T>, T>
{
    public async IAsyncEnumerable<T> Handle(StreamAll<T> request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var item in request.Items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}

public sealed record Unhandled : IStreamRequest<int>;

/// <summary>Closed behavior for <see cref="CountTo"/>: doubles every item.</summary>
public sealed class DoublingBehavior : IStreamPipelineBehavior<CountTo, int>
{
    public async IAsyncEnumerable<int> Handle(CountTo request, StreamHandlerDelegate<int> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            yield return item * 2;
        }
    }
}

/// <summary>Open behavior that records where it runs relative to the stream.</summary>
public sealed class TracingStreamBehavior<TRequest, TResponse>(CallLog log) : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        log.Add("before stream");
        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            yield return item;
        }

        log.Add("after stream");
    }
}
