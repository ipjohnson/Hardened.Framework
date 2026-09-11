using Hardened.Requests.Abstract.Responses;
using MessagePack;
using MessagePack.Formatters;

namespace Hardened.Requests.Serializers.MessagePack.Impl.Formatters;

/// <summary>
/// One of the framework's problem-shaped response bodies, as MessagePack.
/// </summary>
/// <remarks>
/// <para>
/// Fifteen of Hardened's built-in response types have exactly this shape: a <c>detail</c> the
/// caller supplies, and a <c>type</c>, <c>title</c> and <c>status</c> the type itself decides. So
/// they get one formatter parameterised by four accessors rather than fifteen copies of the same
/// sixty lines. The four that carry an extra member have formatters of their own beside this.
/// </para>
/// <para>
/// <b>Named keys, under the keyed mode too.</b> The document publishes no
/// <c>x-message-pack-index</c> for a framework type - it cannot, because the writer that would
/// publish one runs in a generator that does not reference MessagePack - so a client generated from
/// it keys these by name whichever mode it is in. Writing integers here would disagree with every
/// such client on every error body, and the saving is a few bytes on a response that is already the
/// unusual path.
/// </para>
/// <para>
/// The keys and the member order are the JSON representation's, so the two representations describe
/// one document: a caller switching on <c>type</c> reads the same body either way.
/// </para>
/// </remarks>
internal sealed class ProblemFormatter<T> : IMessagePackFormatter<T?> where T : class, IHttpStatusResponse {

    private readonly Func<T, string> _type;
    private readonly Func<T, string> _title;
    private readonly Func<T, string?> _detail;
    private readonly Func<string?, T> _create;

    internal ProblemFormatter(
        Func<T, string> type, Func<T, string> title, Func<T, string?> detail, Func<string?, T> create) {
        _type = type;
        _title = title;
        _detail = detail;
        _create = create;
    }

    public void Serialize(ref MessagePackWriter writer, T? value, MessagePackSerializerOptions options) {
        if (value == null) {
            writer.WriteNil();

            return;
        }

        writer.WriteMapHeader(4);
        writer.Write("detail");
        writer.Write(_detail(value));
        Problem.WriteTail(ref writer, _type(value), _title(value), value.Status);
    }

    public T? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) {
        var count = Problem.MapHeader(ref reader);
        string? detail = null;

        for (var i = 0; i < count; i++) {
            if (reader.ReadString() == "detail") {
                detail = reader.ReadString();
            }
            else {
                // type, title and status are the type's own answers, and a member a later version
                // added is none of this one's business. Both are skipped rather than refused.
                reader.Skip();
            }
        }

        return _create(detail);
    }
}

/// <summary>The three members every problem body ends with, and the reader's counterpart.</summary>
internal static class Problem {

    internal static void WriteTail(ref MessagePackWriter writer, string type, string title, int status) {
        writer.Write("type");
        writer.Write(type);
        writer.Write("title");
        writer.Write(title);
        writer.Write("status");
        writer.Write(status);
    }

    /// <summary>Reads a map header, or 0 for a nil.</summary>
    /// <remarks>
    /// A nil rather than a map is what a peer writes for a null body. Reading it as a zero-member
    /// map hands back an empty instance instead of failing, which is what the JSON side does with
    /// <c>null</c>.
    /// </remarks>
    internal static int MapHeader(ref MessagePackReader reader) =>
        reader.TryReadNil() ? 0 : reader.ReadMapHeader();
}
