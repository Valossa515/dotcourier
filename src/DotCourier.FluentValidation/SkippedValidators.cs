using Microsoft.Extensions.Logging;

namespace DotCourier.FluentValidation;

/// <summary>
/// Generic validators that could not be registered. Logged once, when validation first runs, so a
/// validator that never runs does not go unnoticed.
/// </summary>
internal sealed partial class SkippedValidators
{
    public SkippedValidators(IReadOnlyList<Type> types, ILoggerFactory? loggerFactory)
    {
        if (types.Count == 0 || loggerFactory is null)
        {
            return;
        }

        var logger = loggerFactory.CreateLogger("DotCourier.FluentValidation");
        foreach (var type in types)
        {
            LogSkipped(logger, type.FullName ?? type.Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Courier] Validator {Validator} was not registered: a generic validator is registered only when it validates its own type parameter (e.g. V<T> : AbstractValidator<T>). Register a closed subclass, or add it yourself with services.AddTransient<IValidator<X>, ...>().")]
    private static partial void LogSkipped(ILogger logger, string validator);
}
