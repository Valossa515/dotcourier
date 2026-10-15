using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DotCourier.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace DotCourier.Internal;

/// <summary>
/// Open generic request handlers found by scanning, such as
/// <c>CreateHandler&lt;T&gt; : IRequestHandler&lt;Create&lt;T&gt;, T&gt;</c>. The container cannot close
/// these on its own (the service's type arguments are not the handler's type parameters), so on
/// the first dispatch of a request type the handler's parameters are inferred from it, the
/// constraints are checked, and an activation factory for the closed type is cached.
/// </summary>
/// <remarks>Needs <c>MakeGenericType</c>, so it is empty in compile-time mode (always on under Native AOT).</remarks>
internal sealed class GenericRequestHandlerRegistry(IReadOnlyList<Type> openHandlers)
{
    private const string Guarded = "Empty in compile-time mode, which is always on under Native AOT: nothing is closed at runtime there.";

    private readonly ConcurrentDictionary<Type, ObjectFactory?> _factories = new();

    public IReadOnlyList<Type> OpenHandlers => openHandlers;

    /// <summary>Creates a handler for <paramref name="closedService"/>, or returns <c>null</c> when no open handler fits.</summary>
    public object? Create(Type closedService, IServiceProvider services)
    {
        if (openHandlers.Count == 0)
        {
            return null;
        }

        var factory = _factories.GetOrAdd(closedService, Build);
        return factory?.Invoke(services, null);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = Guarded)]
    private ObjectFactory? Build(Type closedService)
    {
        Type? match = null;
        foreach (var open in openHandlers)
        {
            var closed = TryClose(open, closedService);
            if (closed is null)
            {
                continue;
            }

            if (match is not null)
            {
                throw new InvalidOperationException(
                    $"More than one generic handler fits '{CourierDiagnostics.GetRequestName(closedService.GetGenericArguments()[0])}': " +
                    $"'{CourierDiagnostics.GetRequestName(match)}' and '{CourierDiagnostics.GetRequestName(closed)}'.");
            }

            match = closed;
        }

        return match is null ? null : ActivatorUtilities.CreateFactory(match, Type.EmptyTypes);
    }

    /// <summary>
    /// Closes <paramref name="openHandler"/> so that it implements <paramref name="closedService"/>,
    /// or returns <c>null</c> when its interfaces do not match or its constraints are not met.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Guarded)]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = Guarded)]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = Guarded)]
    internal static Type? TryClose(Type openHandler, Type closedService)
    {
        var definition = closedService.GetGenericTypeDefinition();
        var parameters = openHandler.GetGenericArguments();

        foreach (var pattern in openHandler.GetInterfaces())
        {
            if (!pattern.IsGenericType || pattern.GetGenericTypeDefinition() != definition)
            {
                continue;
            }

            var bindings = new Dictionary<Type, Type>();
            if (!Unify(pattern, closedService, bindings) || !parameters.All(bindings.ContainsKey))
            {
                continue;
            }

            try
            {
                var closed = openHandler.MakeGenericType(parameters.Select(p => bindings[p]).ToArray());
                if (closedService.IsAssignableFrom(closed))
                {
                    return closed;
                }
            }
            catch (ArgumentException)
            {
                // A generic constraint is not satisfied by the inferred arguments: not a match.
            }
        }

        return null;
    }

    /// <summary>Matches a type that mentions generic parameters against a concrete type, recording what each parameter must be.</summary>
    private static bool Unify(Type pattern, Type concrete, Dictionary<Type, Type> bindings)
    {
        if (pattern.IsGenericParameter)
        {
            if (bindings.TryGetValue(pattern, out var bound))
            {
                return bound == concrete;
            }

            bindings[pattern] = concrete;
            return true;
        }

        if (pattern.IsArray)
        {
            return concrete.IsArray
                && pattern.GetArrayRank() == concrete.GetArrayRank()
                && Unify(pattern.GetElementType()!, concrete.GetElementType()!, bindings);
        }

        if (pattern.IsGenericType)
        {
            if (!concrete.IsGenericType || pattern.GetGenericTypeDefinition() != concrete.GetGenericTypeDefinition())
            {
                return false;
            }

            var patternArgs = pattern.GetGenericArguments();
            var concreteArgs = concrete.GetGenericArguments();
            for (var i = 0; i < patternArgs.Length; i++)
            {
                if (!Unify(patternArgs[i], concreteArgs[i], bindings))
                {
                    return false;
                }
            }

            return true;
        }

        return pattern == concrete;
    }
}
