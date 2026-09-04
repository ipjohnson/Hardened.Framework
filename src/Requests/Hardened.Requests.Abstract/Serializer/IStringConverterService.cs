using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Serializer;

public interface IStringConverterService {
    T ParseRequired<T>(string value, string valueName);

    T ParseWithDefault<T>(string value, string valueName, T defaultValue);

    T? ParseOptional<T>(string value, string valueName);

    /// <summary>
    /// A parameter declared as a collection, from every value the request carried under its name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <typeparamref name="TItem"/> is the item type, not the collection type: the generated binder
    /// knows which collection the handler declared and adapts the list this returns.
    /// </para>
    /// <para>
    /// Both spellings of a list are read. A repeated key or header line contributes one item each,
    /// which is OpenAPI's <c>explode: true</c> and the RFC 9110 rule for repeated field lines; a
    /// single value containing commas is split, which is <c>explode: false</c>. Nothing in the model
    /// carries which one the contract asked for, so both are accepted - and an item of a string
    /// collection therefore cannot itself contain a comma.
    /// </para>
    /// </remarks>
    List<TItem> ParseRequiredMany<TItem>(StringValues values, string valueName);

    List<TItem>? ParseOptionalMany<TItem>(StringValues values, string valueName);
}
