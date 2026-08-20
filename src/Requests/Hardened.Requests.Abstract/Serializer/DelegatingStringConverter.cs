namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// One declared type's own parse, as an <see cref="IStringConverter"/>.
/// </summary>
/// <remarks>
/// <para>
/// A path or query parameter arrives as text rather than as JSON, so a generated
/// <c>JsonConverter&lt;T&gt;</c> never saw one. The binder called <c>Enum.Parse</c> on the C# member
/// name instead, which is a different vocabulary from the document's: an enum declaring
/// <c>science-fiction</c> answered 400 for <c>?genre=science-fiction</c> and 200 for
/// <c>?genre=ScienceFiction</c>, a name appearing nowhere in the document. Any declared value that
/// is not already a valid C# identifier was unreachable as a parameter.
/// </para>
/// <para>
/// The generated converter emits a static <c>TryParseWire</c> beside its <c>Read</c> and
/// <c>Write</c>, and this carries it into the binder. A delegate rather than an emitted interface
/// implementation, because <c>ConvertType</c> and a generic <c>Convert&lt;T&gt;</c> are awkward to
/// write through the code emitter and hold nothing that varies but the type.
/// </para>
/// </remarks>
/// <typeparam name="TValue">The type parsed, which is what <see cref="ConvertType"/> reports.</typeparam>
public sealed class DelegatingStringConverter<TValue> : IStringConverter {

    /// <summary>The shape a generated <c>TryParseWire</c> has.</summary>
    public delegate bool TryParse(string value, out TValue parsed);

    private readonly TryParse _tryParse;
    private readonly string _typeName;

    /// <param name="tryParse">The generated parse, usually <c>XConverter.TryParseWire</c>.</param>
    /// <param name="typeName">
    /// What the type is called in a failure message. The document's name for it rather than the C#
    /// one where they differ, since the caller is reading the document.
    /// </param>
    public DelegatingStringConverter(TryParse tryParse, string? typeName = null) {
        _tryParse = tryParse;
        _typeName = typeName ?? typeof(TValue).Name;
    }

    public Type ConvertType => typeof(TValue);

    public T Convert<T>(string value) {
        if (!_tryParse(value, out var parsed)) {
            throw new FormatException($"'{value}' is not a value {_typeName} declares.");
        }

        // Boxed and cast rather than converted per-branch: unboxing to Nullable<TValue> from a boxed
        // TValue is allowed, so one converter serves an optional parameter as well as a required
        // one. StringConverterService unwraps the nullable before it looks this up.
        return (T)(object)parsed!;
    }
}
