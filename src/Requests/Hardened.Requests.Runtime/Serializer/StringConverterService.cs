using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Errors;
using Exception = System.Exception;

namespace Hardened.Requests.Runtime.Serializer;

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
            throw new BadRequestException($"{valueName} was missing");
        }

        try {
            return InternalParseRequired<T>(value);
        }
        catch (Exception e) {
            throw new BadRequestException($"{valueName} is malformed", e);
        }
    }

    public T ParseWithDefault<T>(string value, string valueName, T defaultValue) {
        if (string.IsNullOrEmpty(value)) {
            return defaultValue;
        }

        try {
            return InternalParseRequired<T>(value);
        }
        catch (Exception) {
            return defaultValue;
        }
    }

    public T? ParseOptional<T>(string value, string valueName) {
        if (string.IsNullOrEmpty(value)) {
            return default;
        }

        try {
            return InternalParseRequired<T>(value);
        }
        catch (Exception) {
            return default;
        }
    }

    protected virtual T InternalParseRequired<T>(string value) {
        if (_converters.TryGetValue(typeof(T), out var stringConverter)) {
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
    /// matched nothing, fell through to the throw, and <see cref="ParseOptional{T}"/> swallowed it
    /// and returned null. Optional value-type path and query parameters were silently never bound.
    /// </para>
    /// <para>
    /// Unwrapping once and keeping one table is what stops that recurring: a type added for the
    /// non-nullable case is automatically there for the nullable one, rather than the two lists
    /// drifting apart.
    /// </para>
    /// </remarks>
    protected virtual T StandardConverter<T>(string value) =>
        // Boxed and cast rather than converted per-branch: unboxing to Nullable<T> from a boxed
        // underlying value is allowed, so one table serves both.
        (T)Convert(Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), value);

    private static object Convert(Type type, string value) {
        if (type == typeof(int)) {
            return int.Parse(value);
        }

        if (type == typeof(Guid)) {
            return Guid.Parse(value);
        }

        if (type == typeof(long)) {
            return long.Parse(value);
        }

        if (type == typeof(uint)) {
            return uint.Parse(value);
        }

        if (type == typeof(ulong)) {
            return ulong.Parse(value);
        }

        if (type == typeof(DateTime)) {
            return DateTime.Parse(value);
        }

        if (type == typeof(string)) {
            return value;
        }

        throw new Exception($"Type {type} cannot be converted from string");
    }
}