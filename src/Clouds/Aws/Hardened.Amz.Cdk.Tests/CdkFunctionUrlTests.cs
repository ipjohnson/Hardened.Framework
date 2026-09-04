using Amazon.CDK;
using Amazon.CDK.Assertions;
using Amazon.CDK.AWS.Lambda;
using Hardened.Amz.Cdk.Lambda;
using Hardened.Amz.Shared.Lambda.Runtime.Configuration;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Cdk.Tests;

/// <summary>
/// The one deployment shape that streams: a function behind a function URL, with the URL's invoke
/// mode and the function's response mode written from the same request so the two cannot disagree.
///
/// <para>
/// Synthesised for real and read back through <see cref="Template"/>, because the interesting
/// outcome - what CloudFormation is told - is only observable there.
/// </para>
/// </summary>
[Collection(CdkSynthesis.Name)]
public class CdkFunctionUrlTests : IDisposable {

    private readonly List<string> _directories = [];

    private sealed class Synthesised {
        public required Stack Stack { get; init; }

        public required IStackDeploymentContext Context { get; init; }

        public required LambdaCdkUtil Util { get; init; }

        public Template Template => Template.FromStack(Stack);
    }

    private Synthesised Synth() {
        var directory = Path.Combine(Path.GetTempPath(), "hardened-cdk-url-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _directories.Add(directory);

        var app = new App(new AppProps { Outdir = Path.Combine(directory, "cdk.out") });
        var stack = new Stack(app, "orders-stack");

        var context = new StackDeploymentContext<TestStageConfiguration, StageType, KnownRegion>(
            "orders",
            new TestStageConfiguration(KnownRegion.UsEast1, StageType.Dev),
            new ServiceCollection().BuildServiceProvider()) {
            Stack = stack
        };

        return new Synthesised {
            Stack = stack,
            Context = context,
            Util = new LambdaCdkUtil(new StackContextAccessor { Context = context }, new DefaultFunctionProps()),
        };
    }

    /// <summary>
    /// The default code packages the project with a command that runs the moment it is built, which
    /// has no place in a synth. A directory with one file in it is an asset CDK is happy to stage.
    /// </summary>
    private Func<Code> Asset() {
        var directory = Path.Combine(Path.GetTempPath(), "hardened-cdk-url-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "bootstrap"), "");
        _directories.Add(directory);

        return () => Code.FromAsset(directory);
    }

    private FunctionUrlLambdaRequest UrlRequest(LambdaResponseMode mode) => new() {
        Name = "orders",
        ApplicationType = typeof(CdkFunctionUrlTests),
        ResponseMode = mode,
        Code = Asset(),
    };

    [Fact]
    public void AStreamModeFunctionUrlIsInResponseStreamInvokeMode() {
        var synth = Synth();

        synth.Util.FunctionUrlFunctionCreate(UrlRequest(LambdaResponseMode.Stream));

        synth.Template.HasResourceProperties("AWS::Lambda::Url", new Dictionary<string, object> {
            ["InvokeMode"] = "RESPONSE_STREAM",
        });
    }

    [Fact]
    public void ABufferedFunctionUrlIsInBufferedInvokeMode() {
        var synth = Synth();

        synth.Util.FunctionUrlFunctionCreate(UrlRequest(LambdaResponseMode.Buffered));

        synth.Template.HasResourceProperties("AWS::Lambda::Url", new Dictionary<string, object> {
            ["InvokeMode"] = "BUFFERED",
        });
    }

    /// <summary>
    /// The application reads its mode from this variable, so it is written on every function this
    /// helper creates, buffered included, and it is what the URL's invoke mode was set from.
    /// </summary>
    [Theory]
    [InlineData(LambdaResponseMode.Buffered, "buffered")]
    [InlineData(LambdaResponseMode.Stream, "stream")]
    public void TheResponseModeIsWrittenToTheFunctionsEnvironment(LambdaResponseMode mode, string expected) {
        var synth = Synth();

        synth.Util.FunctionUrlFunctionCreate(UrlRequest(mode));

        synth.Template.HasResourceProperties("AWS::Lambda::Function", new Dictionary<string, object> {
            ["Environment"] = new Dictionary<string, object> {
                ["Variables"] = new Dictionary<string, object> {
                    [LambdaResponseModeConfiguration.EnvironmentVariable] = expected,
                },
            },
        });
    }

    /// <summary>
    /// The caller's <c>Props</c> may assign the environment wholesale; the mode is written after
    /// it, so it cannot be dropped by an application that sets its own variables.
    /// </summary>
    [Fact]
    public void TheResponseModeSurvivesAnEnvironmentTheCallerAssigned() {
        var synth = Synth();
        var request = UrlRequest(LambdaResponseMode.Stream);
        request.Props = props => props.Environment = new Dictionary<string, string> { ["STAGE"] = "dev" };

        synth.Util.FunctionUrlFunctionCreate(request);

        synth.Template.HasResourceProperties("AWS::Lambda::Function", new Dictionary<string, object> {
            ["Environment"] = new Dictionary<string, object> {
                ["Variables"] = new Dictionary<string, object> {
                    ["STAGE"] = "dev",
                    [LambdaResponseModeConfiguration.EnvironmentVariable] = "stream",
                },
            },
        });
    }

    /// <summary>
    /// The URL is on the alias, as the HTTP API integration is, so a CodeDeploy shift moves the
    /// URL's traffic with it. CloudFormation spells that as the function's ARN qualified by the
    /// alias name.
    /// </summary>
    [Fact]
    public void TheUrlAttachesToTheAlias() {
        var synth = Synth();

        synth.Util.FunctionUrlFunctionCreate(UrlRequest(LambdaResponseMode.Stream));

        var template = synth.Template;
        var function = Assert.Single(template.FindResources("AWS::Lambda::Function", new Dictionary<string, object> {
            ["Properties"] = new Dictionary<string, object> {
                ["Handler"] = typeof(CdkFunctionUrlTests).Assembly.GetName().Name!,
            },
        }));

        template.HasResourceProperties("AWS::Lambda::Url", new Dictionary<string, object> {
            ["Qualifier"] = "live",
            ["TargetFunctionArn"] = new Dictionary<string, object> {
                ["Fn::GetAtt"] = new object[] { function.Key, "Arn" },
            },
        });
    }

    [Fact]
    public void TheAuthTypeDefaultsToIam() {
        var synth = Synth();

        synth.Util.FunctionUrlFunctionCreate(UrlRequest(LambdaResponseMode.Stream));

        synth.Template.HasResourceProperties("AWS::Lambda::Url", new Dictionary<string, object> {
            ["AuthType"] = "AWS_IAM",
        });
    }

    /// <summary>
    /// Every Hardened Lambda application carries a generated <c>Main</c> on the AWS bootstrap, and
    /// the managed runtime starts it when the handler names the assembly alone.
    /// </summary>
    [Fact]
    public void TheHandlerIsTheApplicationsAssemblyName() {
        var synth = Synth();

        synth.Util.FunctionUrlFunctionCreate(UrlRequest(LambdaResponseMode.Buffered));

        synth.Template.HasResourceProperties("AWS::Lambda::Function", new Dictionary<string, object> {
            ["Handler"] = typeof(CdkFunctionUrlTests).Assembly.GetName().Name!,
        });
    }

    [Fact]
    public void TheUrlIsRegisteredForOtherStacksToReach() {
        var synth = Synth();

        var (_, url) = synth.Util.FunctionUrlFunctionCreate(UrlRequest(LambdaResponseMode.Stream));

        Assert.Same(url, synth.Context.Get(KnownCdkResources.LambdaFunctionUrl));
        Assert.Equal("orders", synth.Context.GetName(KnownCdkResources.LambdaFunctionUrl));
    }

    [Fact]
    public void TheUrlIsHandedToTheCallerWithItsAlias() {
        var synth = Synth();
        var request = UrlRequest(LambdaResponseMode.Stream);
        Alias? seen = null;
        request.ConfigureUrl = (_, alias) => seen = alias;

        synth.Util.FunctionUrlFunctionCreate(request);

        Assert.Same(synth.Context.Get(KnownCdkResources.LambdaFunctionAlias), seen);
    }

    [Fact]
    public void AFunctionUrlWithoutAnAliasIsRefused() {
        var synth = Synth();
        var request = UrlRequest(LambdaResponseMode.Stream);
        request.AliasName = null;

        var failure = Assert.Throws<InvalidOperationException>(() => synth.Util.FunctionUrlFunctionCreate(request));

        Assert.Contains("AliasName", failure.Message);
    }

    /// <summary>
    /// An HTTP API buffers every response. A stream-mode application behind one writes a prelude
    /// the gateway does not understand, which is a broken deployment rather than a degraded one,
    /// so it is refused before anything is created.
    /// </summary>
    [Fact]
    public void AnHttpApiForAStreamModeApplicationIsRefused() {
        var synth = Synth();
        var request = new HttpApiLambdaRequest {
            Name = "orders",
            ApplicationType = typeof(CdkFunctionUrlTests),
            ResponseMode = LambdaResponseMode.Stream,
            Code = Asset(),
        };

        var failure = Assert.Throws<InvalidOperationException>(() => synth.Util.HttpApiFunctionCreate(request));

        Assert.Contains("FunctionUrlFunctionCreate", failure.Message);
        Assert.Empty(synth.Template.FindResources("AWS::Lambda::Function"));
    }

    [Fact]
    public void ABufferedApplicationBehindAnHttpApiIsWrittenAsBuffered() {
        var synth = Synth();

        synth.Util.HttpApiFunctionCreate(new HttpApiLambdaRequest {
            Name = "orders",
            ApplicationType = typeof(CdkFunctionUrlTests),
            Code = Asset(),
        });

        synth.Template.HasResourceProperties("AWS::Lambda::Function", new Dictionary<string, object> {
            ["Environment"] = new Dictionary<string, object> {
                ["Variables"] = new Dictionary<string, object> {
                    [LambdaResponseModeConfiguration.EnvironmentVariable] = "buffered",
                },
            },
        });
        Assert.Single(synth.Template.FindResources("AWS::ApiGatewayV2::Api"));
    }

    public void Dispose() {
        foreach (var directory in _directories.Where(Directory.Exists)) {
            Directory.Delete(directory, recursive: true);
        }
    }
}
