namespace Hardened.IntegrationTests.Smithy.SUT.Tests;

/// <summary>
/// An awsJson1_0 service, served from the same application as a path-routed one.
/// </summary>
/// <remarks>
/// <para>
/// This is what the protocol seam was for. Every operation here arrives as <c>POST /</c> and is told
/// apart by <c>X-Amz-Target</c>; the pet store next door is routed by path. One generated routing
/// table answers both, and it has to consult the header first - a path tree consulted first would
/// match <c>POST /</c> for whichever bank operation happened to own it and answer the wrong handler
/// for the other.
/// </para>
/// <para>
/// Nothing in the framework was taught what awsJson is. The reader sets a dispatch key and a header
/// on the model; the routing table emits a switch when it sees them.
/// </para>
/// </remarks>
public class BankDispatchTests {

    private static Action<TestWebRequest> Target(string target) =>
        request => request.Headers["X-Amz-Target"] = target;

    [HardenedTest]
    public async Task GetBalance_DispatchesOnTheTargetHeader(ITestWebApp app) {
        var response = await app.Post(new { accountId = "acct-1" }, "/", Target("Bank.GetBalance"));

        response.Assert.Ok();

        var output = response.Deserialize<GetBalanceOutput>();

        Assert.NotNull(output);
        Assert.Equal(12500, output.BalanceCents);
        Assert.Equal("USD", output.Currency);
    }

    /// <summary>
    /// The same route and the same verb as the test above - only the header differs, which is the
    /// whole of how this protocol names an operation.
    /// </summary>
    [HardenedTest]
    public async Task Transfer_DispatchesToADifferentHandlerOnTheSameRoute(ITestWebApp app) {
        var response = await app.Post(
            new { fromAccount = "acct-1", toAccount = "acct-2", amountCents = 500 },
            "/",
            Target("Bank.Transfer"));

        response.Assert.Ok();
        Assert.Equal("acct-1->acct-2:500", response.Deserialize<TransferOutput>()!.TransferId);
    }

    /// <summary>
    /// Every member of the input structure is the body, because a dispatch protocol has nowhere else
    /// to put one and the specification requires binding traits be ignored.
    /// </summary>
    [HardenedTest]
    public async Task Transfer_BindsTheWholeInputStructureFromTheBody(ITestWebApp app) {
        var response = await app.Post(
            new { fromAccount = "a", toAccount = "b", amountCents = 42 },
            "/",
            Target("Bank.Transfer"));

        response.Assert.Ok();
        Assert.Equal("a->b:42", response.Deserialize<TransferOutput>()!.TransferId);
    }

    [HardenedTest]
    public async Task UnknownTarget_IsNotDispatched(ITestWebApp app) {
        var response = await app.Post(new { accountId = "acct-1" }, "/", Target("Bank.NoSuchThing"));

        Assert.NotEqual(200, response.StatusCode);
    }

    [HardenedTest]
    public async Task PostWithNoTargetHeader_IsNotDispatched(ITestWebApp app) {
        var response = await app.Post(new { accountId = "acct-1" }, "/");

        Assert.NotEqual(200, response.StatusCode);
    }

    /// <summary>
    /// The point of the ordering: adding a dispatch protocol to an application does not disturb the
    /// routes already in it.
    /// </summary>
    [HardenedTest]
    public async Task PathRoutedOperationsStillWorkAlongsideDispatch(ITestWebApp app) {
        var routed = await app.Get("/pets/1");
        var dispatched = await app.Post(
            new { accountId = "acct-2" }, "/", Target("Bank.GetBalance"));

        routed.Assert.Ok();
        dispatched.Assert.Ok();

        Assert.Equal("Buddy", routed.Deserialize<GetPetOutput>()!.Pet.Name);
        Assert.Equal(425000, dispatched.Deserialize<GetBalanceOutput>()!.BalanceCents);
    }
}
