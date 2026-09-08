using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing;
using Xunit;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Execution;

public class CloudRunTransportInfoTests {
    private static readonly TestTransportInfo Connection = new((KnownTransportKeys.ClientAddress, "203.0.113.7"));

    [Fact]
    public void TheThreeCloudRunFactsAreAnsweredUnderTheirKeys() {
        var transport = new CloudRunTransportInfo(Connection, "orders", "orders-00042-abc", "orders");

        Assert.Equal("orders", transport.Get(CloudRunTransportInfo.ServiceKey));
        Assert.Equal("orders-00042-abc", transport.Get(CloudRunTransportInfo.RevisionKey));
        Assert.Equal("orders", transport.Get(CloudRunTransportInfo.ConfigurationKey));
    }

    /// <summary>The names are OpenTelemetry's where it has one, so the same fact reads the same under Lambda.</summary>
    [Fact]
    public void TheKeysAreTheFaasConventions() {
        Assert.Equal("faas.name", CloudRunTransportInfo.ServiceKey);
        Assert.Equal("faas.version", CloudRunTransportInfo.RevisionKey);
        Assert.Equal("gcp.cloud_run.configuration", CloudRunTransportInfo.ConfigurationKey);
    }

    [Fact]
    public void EverythingElseIsTheConnections() {
        var transport = new CloudRunTransportInfo(Connection, "orders", null, null);

        Assert.Equal("203.0.113.7", transport.Get(KnownTransportKeys.ClientAddress));
        Assert.Null(transport.Get("a.key.no.transport.publishes"));
    }

    /// <summary>Off Cloud Run nothing sets the variables, and null is the answer rather than an empty string.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnUnsetVariableIsNull(string? value) {
        var transport = new CloudRunTransportInfo(Connection, value, value, value);

        Assert.Null(transport.Get(CloudRunTransportInfo.ServiceKey));
        Assert.Null(transport.Get(CloudRunTransportInfo.RevisionKey));
        Assert.Null(transport.Get(CloudRunTransportInfo.ConfigurationKey));
    }

    [Fact]
    public void TheKeysAreTheConnectionsAndItsOwn() {
        var transport = new CloudRunTransportInfo(Connection, null, null, null);

        Assert.Contains(KnownTransportKeys.ClientAddress, transport.Keys);
        Assert.Contains(CloudRunTransportInfo.ServiceKey, transport.Keys);
        Assert.Contains(CloudRunTransportInfo.RevisionKey, transport.Keys);
        Assert.Contains(CloudRunTransportInfo.ConfigurationKey, transport.Keys);
    }

    [Fact]
    public void TheEnvironmentIsReadWhenNothingIsStated() {
        Environment.SetEnvironmentVariable(CloudRunTransportInfo.ServiceVariable, "from-environment");

        try {
            var transport = new CloudRunTransportInfo(Connection);

            Assert.Equal("from-environment", transport.Get(CloudRunTransportInfo.ServiceKey));
        }
        finally {
            Environment.SetEnvironmentVariable(CloudRunTransportInfo.ServiceVariable, null);
        }
    }
}
