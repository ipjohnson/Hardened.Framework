using System.Globalization;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Validation;
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
    protected virtual T StandardConverter<T>(string value) =>
        // Boxed and cast rather than converted per-branch: unboxing to Nullable<T> from a boxed
        // underlying value is allowed, so one table serves both.
        (T)Convert(Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), value);

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
