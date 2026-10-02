using System.Threading;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// What a contract's <c>info</c> reaches the model as, beyond its title, version and description.
/// </summary>
/// <remarks>
/// Only those three were read, so a contract's license never reached the published document and
/// a linter reading it warned <c>info-license</c>.
/// </remarks>
public class InfoTests
{
    private static ServiceSpecModel Parse(string info)
    {
        var model = OpenApiSpecParser.Parse(
            "openapi: \"3.1.0\"\ninfo:\n  title: T\n  version: \"1.0.0\"\n" + info + "paths: {}\n",
            "spec",
            CancellationToken.None
        );

        Assert.NotNull(model);

        return model!;
    }

    [Fact]
    public void EveryMemberOfInfoReachesTheModel()
    {
        var model = Parse(
            @"  summary: Tickets.
  termsOfService: https://example.com/terms
  contact:
    name: Helpdesk
    url: https://example.com/help
    email: help@example.com
  license:
    name: MIT
    identifier: MIT
  x-audience: public
"
        );

        Assert.Equal("Tickets.", model.InfoSummary);
        Assert.Equal("https://example.com/terms", model.TermsOfService);
        Assert.Equal(
            "{\"name\":\"Helpdesk\",\"url\":\"https://example.com/help\",\"email\":\"help@example.com\"}",
            model.ContactJson
        );
        Assert.Equal("MIT", model.LicenseName);
        Assert.Equal("MIT", model.LicenseIdentifier);
        Assert.Null(model.LicenseUrl);
        Assert.Equal("\"x-audience\":\"public\"", model.InfoExtensionsJson);
    }

    [Fact]
    public void ALicenseUrlIsKeptAsWritten()
    {
        var model = Parse(
            @"  license:
    name: Apache 2.0
    url: https://www.apache.org/licenses/LICENSE-2.0.html
"
        );

        Assert.Equal("Apache 2.0", model.LicenseName);
        Assert.Equal("https://www.apache.org/licenses/LICENSE-2.0.html", model.LicenseUrl);
    }

    [Fact]
    public void AContractThatSaysNothingMoreCarriesNothingMore()
    {
        var model = Parse("");

        Assert.Null(model.InfoSummary);
        Assert.Null(model.TermsOfService);
        Assert.Null(model.ContactJson);
        Assert.Null(model.LicenseName);
        Assert.Null(model.InfoExtensionsJson);
    }
}
