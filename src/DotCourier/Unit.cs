using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCourier;

/// <summary>
/// Represents the absence of a value, used as the response type of requests
/// that produce no result (<see cref="IRequest"/>, <see cref="ICommand"/>).
/// Serializes to JSON <c>null</c>, so a <c>Response&lt;Unit&gt;</c> reads <c>"data": null</c>.
/// </summary>
[JsonConverter(typeof(UnitJsonConverter))]
public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>
{
    /// <summary>The single value of <see cref="Unit"/>.</summary>
    public static readonly Unit Value;

    /// <summary>A completed task holding <see cref="Value"/>.</summary>
    public static Task<Unit> Task { get; } = System.Threading.Tasks.Task.FromResult(Value);

    /// <inheritdoc />
    public bool Equals(Unit other) => true;

    /// <inheritdoc />
    public int CompareTo(Unit other) => 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Unit;

    /// <inheritdoc />
    public override int GetHashCode() => 0;

    /// <inheritdoc />
    public override string ToString() => "()";

    /// <summary>Always <c>true</c>.</summary>
    public static bool operator ==(Unit left, Unit right) => true;

    /// <summary>Always <c>false</c>.</summary>
    public static bool operator !=(Unit left, Unit right) => false;
}

/// <summary>Writes <see cref="Unit"/> as JSON <c>null</c> and reads anything back as <see cref="Unit.Value"/>.</summary>
internal sealed class UnitJsonConverter : JsonConverter<Unit>
{
    public override bool HandleNull => true;

    public override Unit Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        reader.Skip();
        return Unit.Value;
    }

    public override void Write(Utf8JsonWriter writer, Unit value, JsonSerializerOptions options) => writer.WriteNullValue();
}
