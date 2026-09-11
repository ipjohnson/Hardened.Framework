using System.Globalization;
using System.Runtime.CompilerServices;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Validation;
using Microsoft.Extensions.Primitives;
using ValidationModules;
using Exception = System.Exception;

namespace Hardened.Requests.Runtime.Serializer;

/// <summary>
/// Turns path, query and header strings into the types a handler declares.
/// </summary>
/// <remarks>
/// <para>
/// <b>A value that will not parse is an error, not an absent value.</b> Optional parsing used to
/// catch the failure and return null, so <c>?limit=abc</c> and no <c>limit</c> at all were
/// indistinguishable - the request went through with the parameter unset and any constraint on it
/// silently unevaluated. Absent still means null; malformed now fails.
/// </para>
/// <para>
/// <b>Failures report as validation errors.</b> A caller who sent <c>limit=abc</c> and one who sent
/// <c>limit=500</c> have made the same kind of mistake, and reporting one as a bare message and the
/// other as a field-level error would make them look unrelated. Both come back as
/// <c>ValidationError</c>, named by parameter.
/// </para>
/// <para>
/// <b>Parsing is culture-invariant.</b> These are wire values, not user input: <c>1.5</c> is one and
/// a half wherever the server happens to be running.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class StringConverterService : IStringConverterService {

    private readonly Dictionary<Type, IStringConverter> _converters;

    public StringConverterService(IEnumerable<IStringConverter> converters) {
        _converters = new Dictionary<Type, IStringConverter>();

        foreach (var converter in converters) {
            _converters[converter.ConvertType] = converter;
        }
    }

    public T ParseRequired<T>(string value, string valueName) {
        if (string.IsNullOrEmpty(value)) {
            throw Failure(valueName, ValidationCodes.Required, $"{valueName} is required.");
        }

        return Parse<T>(value, valueName);
    }

    public T ParseWithDefault<T>(string value, string valueName, T defaultValue) {
        // Absent takes the default; malformed does not. "Fall back when nothing was sent" and
        // "ignore what was sent because it made no sense" are different, and only the first is what
        // a default is for.
        if (string.IsNullOrEmpty(value)) {
            return defaultValue;
        }

        return Parse<T>(value, valueName);
    }

    public T? ParseOptional<T>(string value, string valueName) {
        if (string.IsNullOrEmpty(value)) {
            return default;
        }

        return Parse<T>(value, valueName);
    }

    public List<TItem> ParseRequiredMany<TItem>(StringValues values, string valueName) {
        var items = Items<TItem>(values, valueName);

        if (items.Count == 0) {
            throw Failure(valueName, ValidationCodes.Required, $"{valueName} is required.");
        }

        return items;
    }

    public List<TItem>? ParseOptionalMany<TItem>(StringValues values, string valueName) {
        if (values.Count == 0) {
            return null;
        }

        return Items<TItem>(values, valueName);
    }

    /// <summary>
    /// Every item the request carried under one name, converted.
    /// </summary>
    /// <remarks>
    /// A repeated key contributes one entry to <paramref name="values"/> and a comma inside an entry
    /// separates two more, so <c>?ids=1&amp;ids=2,3</c> is three items. An empty entry is dropped
    /// rather than converted: <c>?ids=1&amp;ids=&amp;ids=3</c> is two items, not a list with a hole in
    /// it, and a lone <c>?ids=</c> is the empty list rather than a malformed one.
    /// </remarks>
    private List<TItem> Items<TItem>(StringValues values, string valueName) {
        var items = new List<TItem>(values.Count);

        foreach (var value in values) {
            if (string.IsNullOrEmpty(value)) {
                continue;
            }

            foreach (var item in value!.Split(',')) {
                var trimmed = item.Trim();

                if (trimmed.Length == 0) {
                    continue;
                }

                items.Add(Parse<TItem>(trimmed, valueName));
            }
        }

        return items;
    }

    private T Parse<T>(string value, string valueName) {
        try {
            return InternalParseRequired<T>(value);
        }
        catch (Exception exception) when (exception is not Validation.ValidationException) {
            var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            throw Failure(
                valueName, ValidationCodes.Invalid, $"{valueName} is not a valid {type.Name}.", exception);
        }
    }

    private static Validation.ValidationException Failure(
        string field, string code, string message, Exception? inner = null) {
        var result = ValidationResult.FromErrors(new[] { new ValidationError(field, code, message) });

        return inner == null
            ? new Validation.ValidationException(result)
            : new Validation.ValidationException(result, inner);
    }

    protected virtual T InternalParseRequired<T>(string value) {
        if (_converters.TryGetValue(typeof(T), out var stringConverter)) {
            return stringConverter.Convert<T>(value);
        }

        // An optional parameter arrives as Nullable<T>, so a converter registered for the underlying
        // type has to be found through it - otherwise a required enum parameter binds through the
        // description's vocabulary and an optional one silently falls through to Enum.Parse, which
        // answers to a different set of values.
        var underlying = Nullable.GetUnderlyingType(typeof(T));

        if (underlying != null && _converters.TryGetValue(underlying, out stringConverter)) {
            return stringConverter.Convert<T>(value);
        }

        return StandardConverter<T>(value);
    }

    /// <summary>
    /// Converts a string to <typeparamref name="T"/>, which may be a nullable value type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An optional parameter arrives as <c>Nullable&lt;T&gt;</c>, because that is what the generated
    /// binder declares - <c>ParseOptional&lt;int?&gt;</c> rather than <c>ParseOptional&lt;int&gt;</c>.
    /// This used to compare <c>typeof(T)</c> against bare types only, so <c>Nullable&lt;int&gt;</c>
    /// matched nothing and every optional value-type parameter silently bound as null.
    /// </para>
    /// <para>
    /// Unwrapping once and keeping one table is what stops that recurring: a type added for the
    /// non-nullable case is there for the nullable one rather than the two lists drifting.
    /// </para>
    /// </remarks>
    protected virtual T StandardConverter<T>(string value) {
        if (TryStandardValue<T>(value, out var converted)) {
            return converted;
        }

        // The fallback boxes, which is what lets one table serve both shapes: unboxing to
        // Nullable<T> from a boxed underlying value is allowed. Everything the method above does
        // not name arrives here.
        return (T)Convert(Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), value);
    }

    /// <summary>
    /// The types route templates and query strings are actually written in, parsed without a box.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Convert"/> returns <c>object</c>, so every scalar a handler declares used to cost
    /// a box on its way out of the parse - 24 bytes for an <c>int</c>, 32 for a <c>Guid</c>, once
    /// per parameter per request. <see cref="Unsafe.As{TFrom,TTo}"/> reinterprets the parsed value
    /// as <typeparamref name="T"/> instead, which is sound because the comparison immediately above
    /// each call has established that the two are the same type.
    /// </para>
    /// <para>
    /// <b>The comparisons cost nothing for the types they are written for.</b> A value-type
    /// instantiation of a generic method is compiled on its own, so the JIT folds every
    /// <c>typeof(T) ==</c> here to a constant and keeps the one branch that survives.
    /// </para>
    /// <para>
    /// <b><c>string</c> is first for the case where they do cost something.</b> Every reference-type
    /// instantiation shares one compiled body, where <c>typeof(T)</c> is a runtime lookup and the
    /// comparisons genuinely run. <c>string</c> is by far the most common of them and leaves on the
    /// first; a <c>Uri</c> or a <c>byte[]</c> walks the rest and falls through, which is a handful
    /// of pointer comparisons against a parse that allocates anyway.
    /// </para>
    /// <para>
    /// Nullable forms are listed rather than unwrapped, because unwrapping is what needed the box:
    /// an optional parameter arrives as <c>Nullable&lt;T&gt;</c>, and the old path reached it by
    /// unboxing a boxed underlying value. Each pair also skips the
    /// <see cref="Nullable.GetUnderlyingType"/> call below.
    /// </para>
    /// <para>
    /// Everything not here still goes through <see cref="Convert"/> and still boxes. This is the set
    /// a route template or a query string is written in, not every type that can be parsed - adding
    /// one is two lines, and leaving one out costs what it cost before.
    /// </para>
    /// </remarks>
    private static bool TryStandardValue<T>(string value, out T converted) {
        if (typeof(T) == typeof(string)) {
            converted = (T)(object)value;

            return true;
        }

        if (typeof(T) == typeof(int)) {
            var parsed = int.Parse(value, CultureInfo.InvariantCulture);

            converted = Unsafe.As<int, T>(ref parsed);

            return true;
        }

        if (typeof(T) == typeof(int?)) {
            int? parsed = int.Parse(value, CultureInfo.InvariantCulture);

            converted = Unsafe.As<int?, T>(ref parsed);

            return true;
        }

        if (typeof(T) == typeof(long)) {
            var parsed = long.Parse(value, CultureInfo.InvariantCulture);

            converted = Unsafe.As<long, T>(ref parsed);

            return true;
        }

        if (typeof(T) == typeof(long?)) {
            long? parsed = long.Parse(value, CultureInfo.InvariantCulture);

            converted = Unsafe.As<long?, T>(ref parsed);

            return true;
        }

        if (typeof(T) == typeof(Guid)) {
            var parsed = Guid.Parse(value);

            converted = Unsafe.As<Guid, T>(ref parsed);

            return true;
        }

        if (typeof(T) == typeof(Guid?)) {
            Guid? parsed = Guid.Parse(value);

            converted = Unsafe.As<Guid?, T>(ref parsed);

            return true;
        }

        if (typeof(T) == typeof(bool)) {
            var parsed = bool.Parse(value);

            converted = Unsafe.As<bool, T>(ref parsed);

            return true;
        }

        if (typeof(T) == typeof(bool?)) {
            bool? parsed = bool.Parse(value);

            converted = Unsafe.As<bool?, T>(ref parsed);

            return true;
        }

        if (typeof(T) == typeof(DateTime)) {
            var parsed = DateTime.Parse(value, CultureInfo.InvariantCulture);

            converted = Unsafe.As<DateTime, T>(ref parsed);

            return true;
        }

        if (typeof(T) == typeof(DateTime?)) {
            DateTime? parsed = DateTime.Parse(value, CultureInfo.InvariantCulture);

            converted = Unsafe.As<DateTime?, T>(ref parsed);

            return true;
        }

        converted = default!;

        return false;
    }

    /// <summary>
    /// Every type a generated binder can ask for.
    /// </summary>
    /// <remarks>
    /// The set is what <c>TypeMapper</c> produces from a specification plus the primitives a
    /// hand-written handler can declare. A gap here does not fail loudly - it throws, and until
    /// recently optional parsing swallowed that - so it is worth being complete rather than adding
    /// types as they are missed.
    /// </remarks>
    private static object Convert(Type type, string value) {
        if (type == typeof(string)) {
            return value;
        }

        if (type.IsEnum) {
            return Enum.Parse(type, value, ignoreCase: true);
        }

        return Type.GetTypeCode(type) switch {
            TypeCode.Boolean => bool.Parse(value),
            TypeCode.Byte => byte.Parse(value, CultureInfo.InvariantCulture),
            TypeCode.SByte => sbyte.Parse(value, CultureInfo.InvariantCulture),
            TypeCode.Int16 => short.Parse(value, CultureInfo.InvariantCulture),
            TypeCode.UInt16 => ushort.Parse(value, CultureInfo.InvariantCulture),
            TypeCode.Int32 => int.Parse(value, CultureInfo.InvariantCulture),
            TypeCode.UInt32 => uint.Parse(value, CultureInfo.InvariantCulture),
            TypeCode.Int64 => long.Parse(value, CultureInfo.InvariantCulture),
            TypeCode.UInt64 => ulong.Parse(value, CultureInfo.InvariantCulture),
            TypeCode.Single => float.Parse(value, CultureInfo.InvariantCulture),
            TypeCode.Double => double.Parse(value, CultureInfo.InvariantCulture),
            TypeCode.Decimal => decimal.Parse(value, CultureInfo.InvariantCulture),
            TypeCode.Char => char.Parse(value),
            TypeCode.DateTime => DateTime.Parse(value, CultureInfo.InvariantCulture),
            _ => ConvertOther(type, value),
        };
    }

    /// <summary>The types <see cref="TypeCode"/> has nothing to say about.</summary>
    private static object ConvertOther(Type type, string value) {
        if (type == typeof(Guid)) {
            return Guid.Parse(value);
        }

        if (type == typeof(DateOnly)) {
            return DateOnly.Parse(value, CultureInfo.InvariantCulture);
        }

        if (type == typeof(TimeOnly)) {
            return TimeOnly.Parse(value, CultureInfo.InvariantCulture);
        }

        if (type == typeof(DateTimeOffset)) {
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
        }

        if (type == typeof(TimeSpan)) {
            return TimeSpan.Parse(value, CultureInfo.InvariantCulture);
        }

        if (type == typeof(Uri)) {
            return new Uri(value, UriKind.RelativeOrAbsolute);
        }

        // format: byte and format: binary both map to byte[], and base64 is how a spec carries one
        // in a string position.
        if (type == typeof(byte[])) {
            return System.Convert.FromBase64String(value);
        }

        throw new Exception($"Type {type} cannot be converted from string");
    }
}
