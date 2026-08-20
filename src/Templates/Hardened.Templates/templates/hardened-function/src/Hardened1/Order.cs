namespace Hardened1;

/// <summary>
/// The payload this function is invoked with. Deserialised into by the generated entry point,
/// so it is an ordinary class with no attributes and no base type.
/// </summary>
public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }
}
