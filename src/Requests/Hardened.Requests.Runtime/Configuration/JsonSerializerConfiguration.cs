using System.Text.Encodings.Web;
using System.Text.Json;

namespace Hardened.Requests.Runtime.Configuration;

public interface IJsonSerializerConfiguration
{
    JsonSerializerOptions? SerializeOptions { get; }

    JsonSerializerOptions? DeSerializerOptions { get; }
}

public class JsonSerializerConfiguration : IJsonSerializerConfiguration
{
    public JsonSerializerOptions? SerializeOptions { get; set; }

    public JsonSerializerOptions? DeSerializerOptions { get; set; }

    /// <summary>
    /// The options a response serializer writes with when the application set no
    /// <see cref="SerializeOptions"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The relaxed encoder, because the default one escapes every character that is unsafe inside
    /// HTML. An error message then reached the client as <c>'InProgress' is not a value
    /// Priority declares.</c>, which is valid JSON and unreadable in a log or a terminal.
    /// </para>
    /// <para>
    /// The body is served as JSON and never inlined into a page, so the HTML escaping protects
    /// nothing. ASP.NET Core's MVC output formatter makes the same choice for the same reason. An
    /// application that wants the strict encoder sets <see cref="SerializeOptions"/>, and that is
    /// used as given.
    /// </para>
    /// </remarks>
    internal static JsonSerializerOptions DefaultSerializeOptions() =>
        new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
