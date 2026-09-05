using Hardened.Web.Testing;
using Refit;
using Xunit;

namespace Hardened.Refit.Testing.Tests;

/// <summary>
/// Which types the route answers for: an interface with a verb on it, and nothing else.
/// </summary>
/// <remarks>
/// Building one needs the harness's context, which only the harness makes, so construction over
/// the pipeline is asserted in the Web integration application's suite.
/// </remarks>
public class RefitClientRouteTests {

    public interface IHasRoutes {
        [Get("/todos/{id}")]
        Task<IApiResponse<string>> Get(int id);
    }

    /// <summary>The verbs are on the interface it extends, which is still a Refit client.</summary>
    public interface IExtendsRoutes : IHasRoutes;

    public interface INoRoutes {
        Task<string> Get(int id);
    }

    public sealed class NotAnInterface {
        [Get("/todos")]
        public Task<string> Get() => Task.FromResult("");
    }

    private static readonly RefitClientRoute Route = new();

    [Fact]
    public void AnInterfaceWithAVerbIsARefitClient() {
        Assert.True(Route.CanBuild(typeof(IHasRoutes)));
    }

    [Fact]
    public void AnInterfaceInheritingItsVerbsIsOneToo() {
        Assert.True(Route.CanBuild(typeof(IExtendsRoutes)));
    }

    [Fact]
    public void AnInterfaceWithNoVerbIsNot() {
        Assert.False(Route.CanBuild(typeof(INoRoutes)));
    }

    [Fact]
    public void AClassIsNotWhateverItCarries() {
        Assert.False(Route.CanBuild(typeof(NotAnInterface)));
    }

    /// <summary>The harness never asks a route to build what it did not claim; the route says so anyway.</summary>
    [Fact]
    public void BuildingWhatItCannotBuildFailsNamingTheShape() {
        var failure = Assert.Throws<InvalidOperationException>(() => Route.Build(null!, typeof(INoRoutes)));

        Assert.Contains("is not a Refit client", failure.Message);
    }

    [Fact]
    public void TheAttributeNamesTheRoute() {
        TestClientRouteAttribute attribute = new RefitTestingAttribute();

        Assert.Equal(typeof(RefitClientRoute), attribute.RouteType);
    }
}
