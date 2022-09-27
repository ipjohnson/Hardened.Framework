namespace Hardened.Requests.Abstract.Serializer
{
    public interface IStringConverter
    {
        Type ConvertType { get; }

        T Convert<T>(string value);
    }
}
