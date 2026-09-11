using System.Globalization;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Web.Runtime.Responses;
using MessagePack;
using MessagePack.Formatters;

namespace Hardened.Requests.Serializers.MessagePack.Impl.Formatters;

/// <summary>
/// The four built-in response bodies that carry a member beyond <c>detail</c>.
/// </summary>
/// <remarks>
/// Everything <see cref="ProblemFormatter{T}"/>'s remarks say applies here: named keys, in the
/// JSON representation's spelling and order. These are separate only because the shared formatter
/// is parameterised by four accessors and would need a fifth shape for each of them.
/// </remarks>
internal static class WebResponseFormatters {

    /// <summary>
    /// A <c>TimeSpan</c> as the string System.Text.Json writes for one.
    /// </summary>
    /// <remarks>
    /// <c>"00:00:30"</c>, not a number of seconds, because the two representations describe one
    /// document: a schema saying <c>string</c> and a MessagePack body carrying an integer is the
    /// mismatch this whole package exists to avoid.
    /// </remarks>
    internal static void WriteDuration(ref MessagePackWriter writer, TimeSpan value) =>
        writer.Write(value.ToString(null, CultureInfo.InvariantCulture));

    internal static TimeSpan? ReadDuration(ref MessagePackReader reader) {
        if (reader.TryReadNil()) {
            return null;
        }

        return TimeSpan.TryParse(reader.ReadString(), CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}

internal sealed class NotFoundFormatter : IMessagePackFormatter<NotFound?> {

    public static readonly NotFoundFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, NotFound? value, MessagePackSerializerOptions options) {
        if (value == null) {
            writer.WriteNil();

            return;
        }

        writer.WriteMapHeader(5);
        writer.Write("resource");
        writer.Write(value.Resource);
        writer.Write("detail");
        writer.Write(value.Detail);
        Problem.WriteTail(ref writer, value.Type, value.Title, value.Status);
    }

    public NotFound? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) {
        var count = Problem.MapHeader(ref reader);
        var resource = "";
        string? detail = null;

        for (var i = 0; i < count; i++) {
            switch (reader.ReadString()) {
                case "resource":
                    resource = reader.ReadString() ?? "";

                    break;
                case "detail":
                    detail = reader.ReadString();

                    break;
                default:
                    reader.Skip();

                    break;
            }
        }

        return new NotFound(resource, detail);
    }
}

internal sealed class RateLimitedFormatter : IMessagePackFormatter<RateLimited?> {

    public static readonly RateLimitedFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, RateLimited? value, MessagePackSerializerOptions options) {
        if (value == null) {
            writer.WriteNil();

            return;
        }

        writer.WriteMapHeader(5);
        writer.Write("retryAfter");
        WebResponseFormatters.WriteDuration(ref writer, value.RetryAfter);
        writer.Write("detail");
        writer.Write(value.Detail);
        Problem.WriteTail(ref writer, value.Type, value.Title, value.Status);
    }

    public RateLimited? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) {
        var count = Problem.MapHeader(ref reader);
        var retryAfter = TimeSpan.Zero;
        string? detail = null;

        for (var i = 0; i < count; i++) {
            switch (reader.ReadString()) {
                case "retryAfter":
                    retryAfter = WebResponseFormatters.ReadDuration(ref reader) ?? TimeSpan.Zero;

                    break;
                case "detail":
                    detail = reader.ReadString();

                    break;
                default:
                    reader.Skip();

                    break;
            }
        }

        return new RateLimited(retryAfter, detail);
    }
}

internal sealed class ServiceUnavailableFormatter : IMessagePackFormatter<ServiceUnavailable?> {

    public static readonly ServiceUnavailableFormatter Instance = new();

    public void Serialize(
        ref MessagePackWriter writer, ServiceUnavailable? value, MessagePackSerializerOptions options) {
        if (value == null) {
            writer.WriteNil();

            return;
        }

        writer.WriteMapHeader(5);
        writer.Write("after");

        if (value.After is { } after) {
            WebResponseFormatters.WriteDuration(ref writer, after);
        }
        else {
            writer.WriteNil();
        }

        writer.Write("detail");
        writer.Write(value.Detail);
        Problem.WriteTail(ref writer, value.Type, value.Title, value.Status);
    }

    public ServiceUnavailable? Deserialize(
        ref MessagePackReader reader, MessagePackSerializerOptions options) {
        var count = Problem.MapHeader(ref reader);
        TimeSpan? after = null;
        string? detail = null;

        for (var i = 0; i < count; i++) {
            switch (reader.ReadString()) {
                case "after":
                    after = WebResponseFormatters.ReadDuration(ref reader);

                    break;
                case "detail":
                    detail = reader.ReadString();

                    break;
                default:
                    reader.Skip();

                    break;
            }
        }

        return new ServiceUnavailable(after, detail);
    }
}

internal sealed class UnauthorizedFormatter : IMessagePackFormatter<Unauthorized?> {

    public static readonly UnauthorizedFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, Unauthorized? value, MessagePackSerializerOptions options) {
        if (value == null) {
            writer.WriteNil();

            return;
        }

        writer.WriteMapHeader(5);
        writer.Write("detail");
        writer.Write(value.Detail);
        writer.Write("challenge");
        AuthorizationChallengeFormatter.Instance.Serialize(ref writer, value.Challenge, options);
        Problem.WriteTail(ref writer, value.Type, value.Title, value.Status);
    }

    public Unauthorized? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) {
        var count = Problem.MapHeader(ref reader);
        string? detail = null;
        AuthorizationChallenge? challenge = null;

        for (var i = 0; i < count; i++) {
            switch (reader.ReadString()) {
                case "detail":
                    detail = reader.ReadString();

                    break;
                case "challenge":
                    challenge = AuthorizationChallengeFormatter.Instance.Deserialize(ref reader, options);

                    break;
                default:
                    reader.Skip();

                    break;
            }
        }

        return new Unauthorized(detail, challenge);
    }
}

/// <summary>
/// The challenge an <see cref="Unauthorized"/> carries.
/// </summary>
/// <remarks>
/// Every member, because the JSON representation writes every member and the two have to describe
/// one document - but only two of them are read back. The type has no accessible constructor and
/// one public way in: <c>Parse</c>, which derives the scheme, the error, the realm, the scope and
/// the description from the header value. So the round trip goes through the same parser a client
/// reading the <c>WWW-Authenticate</c> header would use, and the other five members are written for
/// the reader that wants them without being trusted on the way back.
/// </remarks>
internal sealed class AuthorizationChallengeFormatter : IMessagePackFormatter<AuthorizationChallenge?> {

    public static readonly AuthorizationChallengeFormatter Instance = new();

    public void Serialize(
        ref MessagePackWriter writer, AuthorizationChallenge? value, MessagePackSerializerOptions options) {
        if (value == null) {
            writer.WriteNil();

            return;
        }

        writer.WriteMapHeader(7);
        writer.Write("statusCode");
        writer.Write(value.StatusCode);
        writer.Write("scheme");
        writer.Write(value.Scheme);
        writer.Write("error");
        writer.Write(value.Error);
        writer.Write("realm");
        writer.Write(value.Realm);
        writer.Write("scope");
        writer.WriteArrayHeader(value.Scope.Count);

        foreach (var scope in value.Scope) {
            writer.Write(scope);
        }

        writer.Write("description");
        writer.Write(value.Description);
        writer.Write("headerValue");
        writer.Write(value.HeaderValue);
    }

    public AuthorizationChallenge? Deserialize(
        ref MessagePackReader reader, MessagePackSerializerOptions options) {
        if (reader.TryReadNil()) {
            return null;
        }

        var count = reader.ReadMapHeader();
        string? headerValue = null;
        var statusCode = 401;

        for (var i = 0; i < count; i++) {
            switch (reader.ReadString()) {
                case "headerValue":
                    headerValue = reader.ReadString();

                    break;
                case "statusCode":
                    statusCode = reader.ReadInt32();

                    break;
                default:
                    reader.Skip();

                    break;
            }
        }

        return headerValue == null ? null : AuthorizationChallenge.Parse(headerValue, statusCode);
    }
}
