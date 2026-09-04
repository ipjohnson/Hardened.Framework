using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Requests.Abstract.Tests.RequestFilter;

/// <summary>
/// The name a registration may give its filter, for the composed-chain log.
/// </summary>
public class RequestFilterInfoTests {

    private sealed class Noop : IExecutionFilter {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    /// <summary>
    /// The registration every existing provider makes. Nothing about it changes, and the name is
    /// left for the log to derive.
    /// </summary>
    [Fact]
    public void ARegistrationThatGivesNoNameHasNone() {
        var info = new RequestFilterInfo(_ => new Noop(), 5);

        Assert.Null(info.Name);
        Assert.Equal(5, info.Order);
        Assert.IsType<Noop>(info.FilterFunc(null!));
    }

    [Fact]
    public void ANamedRegistrationKeepsItsNameAndItsOrder() {
        var info = new RequestFilterInfo(_ => new Noop(), 5, "Noop");

        Assert.Equal("Noop", info.Name);
        Assert.Equal(5, info.Order);
        Assert.IsType<Noop>(info.FilterFunc(null!));
    }
}
