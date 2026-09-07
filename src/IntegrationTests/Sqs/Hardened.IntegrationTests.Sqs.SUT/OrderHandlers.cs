using Hardened.Functions.Runtime.Attributes;

namespace Hardened.IntegrationTests.Sqs.SUT;

public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>
/// A queue handler, written the way an application would write one.
/// </summary>
public class OrderHandlers {
    /// <summary>What each invocation handled, so a test can see the fan-out rather than infer it.</summary>
    public static readonly List<Order> Handled = [];

    /// <summary>Message ids the handler was told to fail, for the partial-failure cases.</summary>
    public static readonly HashSet<string> FailFor = [];

    public static void Reset() {
        Handled.Clear();
        FailFor.Clear();
    }

    /// <summary>
    /// One message, bound to the application's own type.
    /// </summary>
    /// <remarks>
    /// The parameter is an <see cref="Order"/> rather than an SQS message: the batch filter forks
    /// per record and the body of each fork is that record's own body, so binding sees what the
    /// publisher sent rather than the envelope AWS wrapped it in.
    /// </remarks>
    [Queue("orders-new")]
    public void OnOrder(Order order) {
        Handled.Add(order);

        if (FailFor.Contains(order.Id)) {
            throw new InvalidOperationException("handler refused order " + order.Id);
        }
    }
}
