using System.Diagnostics.CodeAnalysis;
using DotCourier.Generated;

namespace DotCourier.Resilience;

/// <summary>Closes the resilience behaviors for compile-time mode (Native AOT), where open generics cannot be closed at runtime.</summary>
internal sealed class ResilienceBehaviorTypes : BehaviorTypes
{
    public static readonly ResilienceBehaviorTypes Instance = new();

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type? CloseRequestBehavior<[DynamicallyAccessedMembers(CompileTimeTypes.Request)] TRequest, TResponse>(Type openBehavior)
    {
        if (openBehavior == typeof(RateLimiterBehavior<,>))
        {
            return typeof(RateLimiterBehavior<TRequest, TResponse>);
        }

        if (openBehavior == typeof(CircuitBreakerBehavior<,>))
        {
            return typeof(CircuitBreakerBehavior<TRequest, TResponse>);
        }

        if (openBehavior == typeof(RetryBehavior<,>))
        {
            return typeof(RetryBehavior<TRequest, TResponse>);
        }

        return openBehavior == typeof(BulkheadBehavior<,>) ? typeof(BulkheadBehavior<TRequest, TResponse>) : null;
    }
}
