using Amazon.DynamoDBv2;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Hardened.Amz.DynamoDbClient.Tests;

/// <summary>
/// The provider exists so that more than one client can exist — a second account, an assumed role,
/// another region — each with its own credentials, built when first asked for rather than when the
/// service collection is assembled.
/// </summary>
public class DynamoDbClientProviderTests {

    [Fact]
    public void ANamedFactoryDecidesEverythingAboutItsClient() {
        var expected = Substitute.For<IAmazonDynamoDB>();

        var provider = Build(options => options.Clients["audit"] = _ => expected);

        Assert.Same(expected, provider.GetClient("audit"));
    }

    [Fact]
    public void NamedClientsAreIndependent() {
        var audit = Substitute.For<IAmazonDynamoDB>();
        var billing = Substitute.For<IAmazonDynamoDB>();

        var provider = Build(options => {
            options.Clients["audit"] = _ => audit;
            options.Clients["billing"] = _ => billing;
        });

        Assert.Same(audit, provider.GetClient("audit"));
        Assert.Same(billing, provider.GetClient("billing"));
    }

    [Fact]
    public void AnUnknownNameFailsLoudlyAndSaysWhatIsConfigured() {
        var provider = Build(options => options.Clients["audit"] = _ => Substitute.For<IAmazonDynamoDB>());

        var error = Assert.Throws<InvalidOperationException>(() => provider.GetClient("typo"));

        Assert.Contains("'typo'", error.Message);
        Assert.Contains("'audit'", error.Message);
    }

    [Fact]
    public void AFactoryRunsOnceAndTheClientIsReused() {
        // AmazonDynamoDBClient owns a connection pool, so building one per call is how sockets run
        // out. Caching is the behaviour, not an optimisation.
        var built = 0;

        var provider = Build(options => options.Clients["audit"] = _ => {
            built++;
            return Substitute.For<IAmazonDynamoDB>();
        });

        provider.GetClient("audit");
        provider.GetClient("audit");

        Assert.Equal(1, built);
    }

    [Fact]
    public void AFactoryIsNotRunUntilItsClientIsAsked() {
        var built = false;

        Build(options => options.Clients["audit"] = _ => {
            built = true;
            return Substitute.For<IAmazonDynamoDB>();
        });

        Assert.False(built, "constructing the provider should not construct any client");
    }

    [Fact]
    public void TheDefaultClientCanBeReplacedWholesale() {
        var expected = Substitute.For<IAmazonDynamoDB>();

        var provider = Build(options => options.DefaultClient = _ => expected);

        Assert.Same(expected, provider.GetClient());
    }

    /// <summary>
    /// The provider owns every client it built, so nothing else can close them. Before it was
    /// IDisposable they lived until the process exited, holding their connection pools open.
    /// </summary>
    [Fact]
    public void DisposingClosesEveryClientItBuilt() {
        var audit = Substitute.For<IAmazonDynamoDB>();
        var billing = Substitute.For<IAmazonDynamoDB>();

        var provider = Build(options => {
            options.Clients["audit"] = _ => audit;
            options.Clients["billing"] = _ => billing;
        });

        provider.GetClient("audit");
        provider.GetClient("billing");
        provider.Dispose();

        audit.Received(1).Dispose();
        billing.Received(1).Dispose();
    }

    /// <summary>
    /// A client never asked for was never built, so there is nothing to close and asking for one
    /// during teardown would be the opposite of what disposal is for.
    /// </summary>
    [Fact]
    public void DisposingDoesNotBuildClientsNobodyAskedFor() {
        var built = false;

        var provider = Build(options => options.Clients["audit"] = _ => {
            built = true;
            return Substitute.For<IAmazonDynamoDB>();
        });

        provider.Dispose();

        Assert.False(built);
    }

    /// <summary>
    /// Dispose has to be idempotent — a container disposing a singleton it also owns a reference to
    /// is ordinary — and it is the cache being cleared that makes it so.
    /// </summary>
    [Fact]
    public void DisposingTwiceClosesEachClientOnce() {
        var client = Substitute.For<IAmazonDynamoDB>();

        var provider = Build(options => options.Clients["audit"] = _ => client);

        provider.GetClient("audit");
        provider.Dispose();
        provider.Dispose();

        client.Received(1).Dispose();
    }

    private static DynamoDbClientProvider Build(Action<DynamoDbOptions> configure) {
        var options = new DynamoDbOptions();
        configure(options);

        return new DynamoDbClientProvider(
            Options.Create<IDynamoDbOptions>(options),
            Substitute.For<IServiceProvider>());
    }
}
