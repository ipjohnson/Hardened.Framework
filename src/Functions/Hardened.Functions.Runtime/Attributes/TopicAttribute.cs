namespace Hardened.Functions.Runtime.Attributes;

/// <summary>
/// Routes messages published to a topic to the attributed handler.
/// </summary>
/// <remarks>
/// <para>
/// SNS on AWS, Service Bus topics on Azure, Pub/Sub on Google. Fan-out rather than the
/// point-to-point delivery <see cref="QueueAttribute"/> describes: every subscriber sees every
/// message, and nothing is competing for it.
/// </para>
/// <para>
/// Routes as <c>TOPIC /order-events</c>.
/// </para>
/// </remarks>
public class TopicAttribute : Attribute {
    public TopicAttribute(string name) {
        Name = name;
    }

    /// <summary>The topic's own name, not an ARN.</summary>
    public string Name { get; }
}
