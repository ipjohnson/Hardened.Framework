namespace Hardened1;

#if (blob)
/// <summary>
/// What the store says about an object that changed. Bound from the notification, so an ordinary
/// class with no attributes and no base type.
/// </summary>
public class Upload {
    public string Bucket { get; set; } = "";

    public string Key { get; set; } = "";

    public long Size { get; set; }
}
#else
/// <summary>
/// The payload. Bound into by the host, so an ordinary class with no attributes and no base type.
/// </summary>
public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }
}
#endif
