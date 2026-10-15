using System.Collections;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DotCourier.SourceGenerator;

/// <summary>What one class contributes to the registrations. Value-equal, so unchanged classes are not reprocessed.</summary>
internal sealed record HandlerType(
    string Name,
    bool IsOpenGeneric,
    bool IsAccessible,
    EquatableArray<Registration> Registrations,
    LocationInfo? Location);

/// <summary>One call on the generated registration context.</summary>
/// <param name="Kind">Which context method to call.</param>
/// <param name="TypeArguments">The method's generic arguments, fully qualified.</param>
/// <param name="ServiceKey">Identifies the service, to detect two handlers for the same request.</param>
internal sealed record Registration(RegistrationKind Kind, EquatableArray<string> TypeArguments, string ServiceKey);

internal enum RegistrationKind
{
    RequestHandler,
    StreamHandler,
    NotificationHandler,
    Service,
}

internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public static LocationInfo? From(Location? location) =>
        location is { IsInSource: true } ? new LocationInfo(location.SourceTree!.FilePath, location.SourceSpan, location.GetLineSpan().Span) : null;
}

/// <summary>An immutable array compared by its items, for the generator's incremental cache.</summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items = items;

    public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public bool Equals(EquatableArray<T> other) => Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in Items)
        {
            hash = (hash * 31) + (item?.GetHashCode() ?? 0);
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
