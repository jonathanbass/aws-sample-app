using System.Text.Json;

namespace Contracts;

/// <summary>
/// The one serializer configuration for every event on the wire.
/// </summary>
/// <remarks>
/// The publisher and the consumer MUST use this same instance. System.Text.Json
/// defaults to PascalCase and binds case-sensitively, so a mismatch between the
/// two ends does not throw - it binds every field to its default. The failure
/// then looks like an empty message, not a serialization fault.
/// </remarks>
public static class EventJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
