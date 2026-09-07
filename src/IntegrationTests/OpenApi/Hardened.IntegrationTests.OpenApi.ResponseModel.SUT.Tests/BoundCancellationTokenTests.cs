using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Hardened.Web.Runtime.Responses;

namespace Hardened.IntegrationTests.OpenApi.ResponseModel.SUT.Tests;

/// <summary>
/// The described half of <c>$(HardenedBindCancellationToken)</c>, driven through a real request.
/// </summary>
/// <remarks>
/// <para>
/// A described handler implements a signature it did not write, so this parameter is the only way
/// the request's token reaches one. This project sets the property; the sibling SUT leaves it off,
/// so the two together build and dispatch against both shapes of the generated interface.
/// </para>
/// <para>
/// That the project compiles at all is most of the assertion: the interface is written by the build
/// task and the call that fills it by the source generator, and a flag reaching one and not the
/// other is a call whose arguments do not match the method it calls. What compiling cannot say is
/// which token arrived, which is what the test below is for.
/// </para>
/// </remarks>
public class BoundCancellationTokenTests {

    /// <summary>
    /// The operation carries a <c>[Timeout]</c>, so the token bound to it is the budget's rather
    /// than <c>CancellationToken.None</c> - which compiles just as well and never fires.
    /// </summary>
    [HardenedTest]
    public async Task ABoundedHandlerIsHandedATokenThatCanFire(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/labels/abc");

        response.Assert.Ok();

        Assert.True(LabelServiceImpl.LastTokenCanBeCanceled);
    }

    /// <summary>
    /// The other half, in the same handler: what the token cannot say is how long there was.
    /// </summary>
    /// <remarks>
    /// Taken through the constructor, which is the only way into a described handler, and resolved
    /// from a singleton registration so the handler's own lifetime does not decide whether it can
    /// ask.
    /// </remarks>
    [HardenedTest]
    public async Task ABoundedHandlerCanReadItsDeadline(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/labels/abc");

        response.Assert.Ok();

        Assert.NotNull(LabelServiceImpl.LastDeadlineRemainingMs);
        Assert.InRange(LabelServiceImpl.LastDeadlineRemainingMs!.Value, 0, 30_000);
    }

    /// <summary>
    /// The two routes into a described handler agree. The parameter is bound off the context by the
    /// generated dispatch and the accessor is published by the filter, and both are meant to be the
    /// token the budget cancels.
    /// </summary>
    [HardenedTest]
    public async Task TheAccessorCarriesTheSameTokenThatWasBound(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/labels/abc");

        response.Assert.Ok();

        Assert.True(LabelServiceImpl.AccessorTokenMatchedTheBoundOne);
    }
}
