namespace Hardened.Requests.Abstract.RequestFilter;

public static class FilterOrder {
    public const int BeforeSerialization = Serialization - 1;

    public const int Serialization = 5;

    public const int DefaultValue = 1000;

    public const int EndPointHandlers = DefaultValue * 2;

    public const int EndPointInvoke = DefaultValue * 2;
}