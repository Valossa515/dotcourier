using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;

namespace DotCourier.Resilience;

/// <summary>Runs the rest of the pipeline inside the request type's Polly pipeline for one strategy.</summary>
internal abstract class ResilienceBehavior<TRequest, TResponse>(CourierResiliencePipelines pipelines, ResilienceStrategy strategy, CourierOptions? options)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var pipeline = pipelines.Get(typeof(TRequest), strategy);
        return pipeline is null ? next(cancellationToken) : Execute(pipeline, next, cancellationToken);
    }

    private async Task<TResponse> Execute(ResiliencePipeline pipeline, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await pipeline.ExecuteAsync(token => new ValueTask<TResponse>(next(token)), cancellationToken).ConfigureAwait(false);
        }
        catch (BrokenCircuitException ex)
        {
            pipelines.RecordRejection(typeof(TRequest), strategy);
            throw new CircuitOpenException(typeof(TRequest), ex.RetryAfter, ex, options?.Messages);
        }
        catch (RateLimiterRejectedException ex) when (strategy == ResilienceStrategy.RateLimiter)
        {
            pipelines.RecordRejection(typeof(TRequest), strategy);
            throw new RateLimitExceededException(typeof(TRequest), ex.RetryAfter, ex, options?.Messages);
        }
        catch (RateLimiterRejectedException ex) when (strategy == ResilienceStrategy.Bulkhead)
        {
            pipelines.RecordRejection(typeof(TRequest), strategy);
            throw new BulkheadRejectedException(typeof(TRequest), ex, options?.Messages);
        }
    }
}

internal sealed class RateLimiterBehavior<TRequest, TResponse>(CourierResiliencePipelines pipelines, CourierOptions? options = null)
    : ResilienceBehavior<TRequest, TResponse>(pipelines, ResilienceStrategy.RateLimiter, options)
    where TRequest : notnull;

internal sealed class CircuitBreakerBehavior<TRequest, TResponse>(CourierResiliencePipelines pipelines, CourierOptions? options = null)
    : ResilienceBehavior<TRequest, TResponse>(pipelines, ResilienceStrategy.CircuitBreaker, options)
    where TRequest : notnull;

internal sealed class RetryBehavior<TRequest, TResponse>(CourierResiliencePipelines pipelines, CourierOptions? options = null)
    : ResilienceBehavior<TRequest, TResponse>(pipelines, ResilienceStrategy.Retry, options)
    where TRequest : notnull;

internal sealed class BulkheadBehavior<TRequest, TResponse>(CourierResiliencePipelines pipelines, CourierOptions? options = null)
    : ResilienceBehavior<TRequest, TResponse>(pipelines, ResilienceStrategy.Bulkhead, options)
    where TRequest : notnull;
