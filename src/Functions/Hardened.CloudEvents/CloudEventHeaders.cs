using Microsoft.Extensions.Primitives;

namespace Hardened.CloudEvents;

/// <summary>
/// An event's context attributes as headers on the request a handler meets.
/// </summary>
/// <remarks>
/// <para>
/// The binary form's own names - <c>ce-id</c>, <c>ce-source</c>, <c>ce-type</c> and the rest -
/// whichever form the event arrived in, so a handler that reads the event id reads one header on
/// Cloud Run and on Azure, and reads it the same whether Eventarc sent binary mode or Event Grid
/// sent structured. An adapter writes these beside whatever headers its own payload gives.
/// </para>
/// <para>
/// The payload's content type is not written here. It belongs under <c>Content-Type</c>, which is
/// the adapter's to set from <see cref="CloudEvent.DataContentType"/> when it knows what the body
/// it hands on is.
/// </para>
/// </remarks>
public static class CloudEventHeaders {
    public const string SpecVersion = "ce-specversion";
    public const string Id = "ce-id";
    public const string Source = "ce-source";
    public const string Type = "ce-type";
    public const string Subject = "ce-subject";
    public const string Time = "ce-time";
    public const string DataSchema = "ce-dataschema";

    /// <summary>
    /// Writes <paramref name="cloudEvent"/>'s attributes onto <paramref name="headers"/>: the four
    /// required ones, each optional one that is set, and every extension under its prefixed name.
    /// </summary>
    public static void Write(IDictionary<string, StringValues> headers, CloudEvent cloudEvent) {
        headers[SpecVersion] = cloudEvent.SpecVersion;
        headers[Id] = cloudEvent.Id;
        headers[Source] = cloudEvent.Source;
        headers[Type] = cloudEvent.Type;

        Set(headers, Subject, cloudEvent.Subject);
        Set(headers, Time, cloudEvent.Time);
        Set(headers, DataSchema, cloudEvent.DataSchema);

        foreach (var extension in cloudEvent.Extensions) {
            headers[CloudEventReader.HeaderPrefix + extension.Key] = extension.Value;
        }
    }

    private static void Set(IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }
}
