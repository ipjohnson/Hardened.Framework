using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Errors;

namespace Hardened.Requests.Runtime.Serializer
{
    public class StringConverterService : IStringConverterService
    {
        private Dictionary<Type, IStringConverter> _converters;

        public StringConverterService(IEnumerable<IStringConverter> converters)
        {
            _converters = new Dictionary<Type, IStringConverter>();

            foreach (var converter in converters)
            {
                _converters[converter.ConvertType] = converter;
            }
        }


        public T ParseRequired<T>(string value, string valueName)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new BadRequestException($"{valueName} was missing");
            }

            return InternalParseRequired<T>(value);
        }

        public T ParseWithDefault<T>(string value, string valueName, T defaultValue)
        {
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            try
            {
                return InternalParseRequired<T>(value);
            }
            catch
            {
                return defaultValue;
            }
        }

        public T? ParseOptional<T>(string value, string valueName)
        {
            if (string.IsNullOrEmpty(value))
            {
                return default;
            }

            try
            {
                return InternalParseRequired<T>(value);
            }
            catch
            {
                return default;
            }
        }

        protected virtual T InternalParseRequired<T>(string value)
        {
            if (_converters.TryGetValue(typeof(T), out var stringConverter))
            {
                return stringConverter.Convert<T>(value);
            }

            return StandardConverter<T>(value);
        }

        protected virtual T StandardConverter<T>(string value)
        {
            if (typeof(T) == typeof(int))
            {
                return (T)(object)int.Parse(value);
            }

            if (typeof(T) == typeof(Guid))
            {
                return (T)(object)Guid.Parse(value);
            }

            if (typeof(T) == typeof(long))
            {
                return (T)(object)long.Parse(value);
            }

            if (typeof(T) == typeof(uint))
            {
                return (T)(object)uint.Parse(value);
            }

            if (typeof(T) == typeof(ulong))
            {
                return (T)(object)ulong.Parse(value);
            }

            if (typeof(T) == typeof(DateTime))
            {
                return (T)(object)DateTime.Parse(value);
            }

            if (typeof(T) == typeof(string))
            {
                return (T)(object)value;
            }

            throw new Exception($"Type {typeof(T)} cannot be converted from string");
        }
        
    }
}
