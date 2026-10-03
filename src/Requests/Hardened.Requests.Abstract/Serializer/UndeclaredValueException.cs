using System.Text.Json;

namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// A value the enum's description or document does not declare.
/// </summary>
/// <remarks>
/// Its own type so the request deserializer can tell it from a body that is not JSON, which no
/// second read would get past. See <see cref="UndeclaredValue"/>.
/// </remarks>
public sealed class UndeclaredValueException : JsonException
{
    public UndeclaredValueException(string message)
        : base(message) { }
}
