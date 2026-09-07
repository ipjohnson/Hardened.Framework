using Hardened.Requests.Abstract.Attributes;

namespace Hardened.IntegrationTests.Invoke.SUT;

/// <summary>
/// A payload with fields named the way AWS names its own.
/// </summary>
/// <remarks>
/// <c>Records</c> and <c>RequestContext</c> are deliberate. They are the fields SQS, SNS, DynamoDB
/// Streams, Kinesis and API Gateway are recognised by, and a caller is entitled to both - which is
/// the whole reason direct invoke gets a function of its own instead of being told apart by
/// inspection.
/// </remarks>
public class OrderRequest {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }

    public List<string> Records { get; set; } = [];

    public string RequestContext { get; set; } = "";
}

public class OrderReceipt {
    public string Id { get; set; } = "";

    public string Status { get; set; } = "";
}

/// <summary>
/// What the handler did with a request.
/// </summary>
/// <remarks>
/// Injected rather than a static list, so each test sees only its own invocations.
/// </remarks>
public interface IOrderLog {
    void Placed(OrderRequest request);
}

public class PlaceOrder {
    /// <summary>
    /// Unnamed, which is what makes it answer whatever the deployment called the function.
    /// </summary>
    /// <remarks>
    /// A directly invoked function hosts one operation, so there is no name worth matching and the
    /// generator emits no switch at all. Naming it would tie the handler to the Lambda's own name,
    /// which is the deployment's to choose.
    /// </remarks>
    [HardenedFunction]
    public OrderReceipt Handle(OrderRequest request, IOrderLog log) {
        log.Placed(request);

        return new OrderReceipt { Id = request.Id, Status = "placed" };
    }
}
