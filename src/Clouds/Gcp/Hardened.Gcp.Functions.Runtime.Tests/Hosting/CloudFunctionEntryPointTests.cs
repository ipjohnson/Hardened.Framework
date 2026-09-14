using Google.Cloud.Functions.Framework;
using Google.Cloud.Functions.Hosting;
using Hardened.Gcp.Functions.Runtime.Hosting;
using Xunit;

namespace Hardened.Gcp.Functions.Runtime.Tests.Hosting;

/// <summary>
/// What the generator wrote, asked of it the way the Functions Framework asks.
/// </summary>
/// <remarks>
/// Every assertion here goes through the framework's own public API rather than restating the
/// generator's output. <c>Assembly.GetType</c> is how <c>HostingInternals.GetFunctionTarget</c>
/// resolves <c>FUNCTION_TARGET</c>, and <c>FunctionsStartupAttribute.GetStartupTypes</c> is how
/// the startup classes are found and how <c>UseFunctionsFramework</c> validates them against the
/// ones that actually ran. A generated name that stopped matching either would fail at deploy,
/// which is the failure these are here to move forward to the build.
/// </remarks>
public class CloudFunctionEntryPointTests
{
    /// <summary>
    /// The name a deploy script writes as <c>--entry-point</c>, and the lookup gen2 performs on
    /// it.
    /// </summary>
    private const string Target = "Hardened.Gcp.Functions.Runtime.Tests.FunctionsAppCloudFunction";

    private static Type EntryPoint =>
        typeof(FunctionsApp).Assembly.GetType(Target)
        ?? throw new InvalidOperationException($"No type named {Target}.");

    [Fact]
    public void TheEntryTypeIsTheApplicationsNameAndNamespace()
    {
        Assert.NotNull(typeof(FunctionsApp).Assembly.GetType(Target));
    }

    /// <summary>
    /// <c>IHttpFunction</c> and not <c>ICloudEventFunction</c>. The typed interface would put
    /// Google's CloudEvent deserializer in front of Hardened's and the six envelope decoders would
    /// stop being the single path; the raw request has to reach the front door.
    /// </summary>
    [Fact]
    public void TheEntryTypeIsAnHttpFunction()
    {
        Assert.True(typeof(IHttpFunction).IsAssignableFrom(EntryPoint));
    }

    /// <summary>
    /// The framework builds the target through the container, so a type it cannot construct is a
    /// start-up failure rather than a compile error.
    /// </summary>
    [Fact]
    public void TheEntryTypeTakesOnlyWhatTheStartupRegisters()
    {
        var constructor = Assert.Single(EntryPoint.GetConstructors());

        var parameter = Assert.Single(constructor.GetParameters());

        Assert.Equal(typeof(CloudFunctionHost), parameter.ParameterType);
    }

    [Fact]
    public void TheStartupIsFoundWhereTheFrameworkLooks()
    {
        var startups = FunctionsStartupAttribute.GetStartupTypes(
            typeof(FunctionsApp).Assembly,
            EntryPoint
        );

        Assert.Equal(new[] { typeof(HardenedFunctionsStartup<FunctionsApp>) }, startups);
    }

    /// <summary>
    /// <c>UseFunctionsStartups</c> constructs the startup itself, with no container and no
    /// arguments.
    /// </summary>
    [Fact]
    public void TheStartupIsConstructibleTheWayTheFrameworkConstructsIt()
    {
        var startups = FunctionsStartupAttribute.GetStartupTypes(
            typeof(FunctionsApp).Assembly,
            EntryPoint
        );

        var startup = Activator.CreateInstance(startups.Single());

        Assert.IsType<HardenedFunctionsStartup<FunctionsApp>>(startup);
    }
}
