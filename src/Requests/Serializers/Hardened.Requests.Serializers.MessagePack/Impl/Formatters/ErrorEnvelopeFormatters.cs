using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Runtime.Validation;
using MessagePack;
using MessagePack.Formatters;

namespace Hardened.Requests.Serializers.MessagePack.Impl.Formatters;

/// <summary>
/// The framework's own error envelopes, as MessagePack.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written by hand because the types cannot carry the attribute.</b> They live in
/// <c>Hardened.Requests.Abstract</c> and <c>Hardened.Requests.Runtime</c>, which every application
/// references and neither of which is going to take a dependency on MessagePack for two classes.
/// MessagePack's source generator writes a formatter only for a type marked
/// <c>[MessagePackObject]</c>, so the formatter has to be written where the dependency already is.
/// </para>
/// <para>
/// Without them an operation declaring <c>application/x-msgpack</c> answered <b>500 with an empty
/// body</b> for every refusal it had to report - a bind failure, a validation failure, an
/// authorization refusal. <c>ExceptionResponseSerializer</c> negotiates the error body through the
/// same locator the success goes through, so the MessagePack writer was chosen, found no formatter
/// for <c>RequestValidationError</c>, and threw inside the handler that exists to answer a throw.
/// The document has described the error body as MessagePack since the media types landed; this is
/// the runtime agreeing with it.
/// </para>
/// <para>
/// Map style, with the keys the JSON representation uses. The two representations describe one
/// document, and a caller switching on <c>type</c> reads the same envelope either way - which is
/// the whole reason the envelopes have a documented shape.
/// </para>
/// </remarks>
internal sealed class ErrorModelFormatter : IMessagePackFormatter<ErrorModel?> {

    public static readonly ErrorModelFormatter Instance = new();

    public void Serialize(
        ref MessagePackWriter writer, ErrorModel? value, MessagePackSerializerOptions options) {
        if (value == null) {
            writer.WriteNil();

            return;
        }

        writer.WriteMapHeader(3);
        writer.Write("type");
        writer.Write(value.Type);
        writer.Write("message");
        writer.Write(value.Message);
        writer.Write("details");
        writer.Write(value.Details);
    }

    public ErrorModel? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) {
        var count = Problem.MapHeader(ref reader);
        var model = new ErrorModel();

        for (var i = 0; i < count; i++) {
            switch (reader.ReadString()) {
                case "type":
                    model.Type = reader.ReadString() ?? "";

                    break;
                case "message":
                    model.Message = reader.ReadString() ?? "";

                    break;
                case "details":
                    model.Details = reader.ReadString() ?? "";

                    break;
                default:
                    // A member this version does not know, skipped rather than refused. A peer on a
                    // newer framework writes a longer map, and an envelope is not worth a break.
                    reader.Skip();

                    break;
            }
        }

        return model;
    }
}

internal sealed class RequestValidationErrorFormatter : IMessagePackFormatter<RequestValidationError?> {

    public static readonly RequestValidationErrorFormatter Instance = new();

    public void Serialize(
        ref MessagePackWriter writer, RequestValidationError? value,
        MessagePackSerializerOptions options) {
        if (value == null) {
            writer.WriteNil();

            return;
        }

        writer.WriteMapHeader(3);
        writer.Write("type");
        writer.Write(value.Type);
        writer.Write("message");
        writer.Write(value.Message);
        writer.Write("errors");
        writer.WriteArrayHeader(value.Errors.Count);

        foreach (var error in value.Errors) {
            RequestValidationFieldErrorFormatter.Instance.Serialize(ref writer, error, options);
        }
    }

    public RequestValidationError? Deserialize(
        ref MessagePackReader reader, MessagePackSerializerOptions options) {
        var count = Problem.MapHeader(ref reader);
        var model = new RequestValidationError();

        for (var i = 0; i < count; i++) {
            switch (reader.ReadString()) {
                case "type":
                    model.Type = reader.ReadString() ?? "";

                    break;
                case "message":
                    model.Message = reader.ReadString() ?? "";

                    break;
                case "errors":
                    if (reader.TryReadNil()) {
                        break;
                    }

                    var errors = reader.ReadArrayHeader();

                    for (var e = 0; e < errors; e++) {
                        var field = RequestValidationFieldErrorFormatter.Instance
                            .Deserialize(ref reader, options);

                        if (field != null) {
                            model.Errors.Add(field);
                        }
                    }

                    break;
                default:
                    reader.Skip();

                    break;
            }
        }

        return model;
    }
}

internal sealed class RequestValidationFieldErrorFormatter
    : IMessagePackFormatter<RequestValidationFieldError?> {

    public static readonly RequestValidationFieldErrorFormatter Instance = new();

    public void Serialize(
        ref MessagePackWriter writer, RequestValidationFieldError? value,
        MessagePackSerializerOptions options) {
        if (value == null) {
            writer.WriteNil();

            return;
        }

        writer.WriteMapHeader(3);
        writer.Write("field");
        writer.Write(value.Field);
        writer.Write("code");
        writer.Write(value.Code);
        writer.Write("message");
        writer.Write(value.Message);
    }

    public RequestValidationFieldError? Deserialize(
        ref MessagePackReader reader, MessagePackSerializerOptions options) {
        var count = Problem.MapHeader(ref reader);
        var model = new RequestValidationFieldError();

        for (var i = 0; i < count; i++) {
            switch (reader.ReadString()) {
                case "field":
                    model.Field = reader.ReadString() ?? "";

                    break;
                case "code":
                    model.Code = reader.ReadString() ?? "";

                    break;
                case "message":
                    model.Message = reader.ReadString() ?? "";

                    break;
                default:
                    reader.Skip();

                    break;
            }
        }

        return model;
    }
}

