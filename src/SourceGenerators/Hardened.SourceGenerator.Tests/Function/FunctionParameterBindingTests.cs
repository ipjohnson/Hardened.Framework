using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Function;

/// <summary>
/// Where each parameter of a function handler comes from.
///
/// <para>
/// A function has no route, so the binding rules a web handler leans on — path tokens, query string
/// — do not apply. What is left is the rule that matters for a function: a plain class is the
/// payload, an interface is a service, and the execution types are handed over directly. Getting
/// this wrong does not fail to compile, it deserialises the request body into a service.
/// </para>
/// </summary>
public class FunctionParameterBindingTests {

    /// <summary>
    /// The payload. Anything that is not an interface and not one of the execution types is
    /// deserialised from the request body — this is the parameter SqsTest's <c>DataModel</c> is.
    /// </summary>
    [Fact]
    public void AModelParameterIsDeserialisedFromTheRequestBody() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process(DataModel model) { }
            """, FunctionGeneratorHarness.SupportTypes))
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        Assert.Contains(
            "parameters.model = (await contentSerializationService.DeserializeRequestBody<global::TestApp.DataModel>(context))!;",
            source);
    }

    /// <summary>
    /// An interface parameter is resolved from the request's service provider, not from the body.
    /// A payload model is a class and a service is an interface, and that is the whole distinction.
    /// </summary>
    [Fact]
    public void AnInterfaceParameterIsResolvedFromTheServiceProvider() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process(IThing thing) { }
            """, FunctionGeneratorHarness.SupportTypes))
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        Assert.Contains(
            "parameters.thing = context.RequestServices.GetRequiredService<global::TestApp.IThing>();",
            source);

        Assert.DoesNotContain("DeserializeRequestBody<global::TestApp.IThing>", source);
    }

    /// <summary>
    /// <c>IExecutionContext</c> is the context already in hand, assigned straight across. It is an
    /// interface, so it has to be recognised before the general interface rule or it would be
    /// resolved from DI — where nothing registers it.
    /// </summary>
    [Fact]
    public void AnExecutionContextParameterIsHandedTheContextItself() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process(IExecutionContext context) { }
            """)).AssertNoErrors().SourceContaining("Process.FunctionHandler");

        Assert.Contains("parameters.context = context;", source);
        Assert.DoesNotContain("GetRequiredService<global::Hardened.Requests.Abstract.Execution.IExecutionContext>",
            source);
    }

    [Fact]
    public void AServiceProviderParameterIsHandedTheRequestServices() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process(IServiceProvider provider) { }
            """)).AssertNoErrors().SourceContaining("Process.FunctionHandler");

        Assert.Contains("parameters.provider = context.RequestServices;", source);
    }

    [Fact]
    public void AnExecutionRequestParameterIsHandedTheRequest() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process(IExecutionRequest request) { }
            """)).AssertNoErrors().SourceContaining("Process.FunctionHandler");

        Assert.Contains("parameters.request = context.Request;", source);
    }

    [Fact]
    public void AnExecutionResponseParameterIsHandedTheResponse() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process(IExecutionResponse response) { }
            """)).AssertNoErrors().SourceContaining("Process.FunctionHandler");

        Assert.Contains("parameters.response = context.Response;", source);
    }

    /// <summary>
    /// Every binding source a function handler has, in one signature. Each is decided
    /// independently, so this is the case where one rule shadowing another shows up — and it is the
    /// shape a real handler that needs both its payload and a service actually has.
    /// </summary>
    [Fact]
    public void AllBindingSourcesCombineInASingleHandler() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public async Task<string> Process(
                    DataModel model, IThing thing, IExecutionContext context, IServiceProvider provider) {
                    await Task.Yield();
                    return model.Value + thing.Describe();
                }
            """, FunctionGeneratorHarness.SupportTypes))
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        Assert.Contains("DeserializeRequestBody<global::TestApp.DataModel>", source);
        Assert.Contains("GetRequiredService<global::TestApp.IThing>", source);
        Assert.Contains("parameters.context = context;", source);
        Assert.Contains("parameters.provider = context.RequestServices;", source);
    }

    /// <summary>
    /// <c>[FromContext]</c> binds out of the request headers, which is how the Lambda runtime
    /// carries the invocation context. The attribute itself ships in
    /// <c>Hardened.Amz.Function.Lambda.Runtime</c> — Hardened.Framework cannot reference it without
    /// inverting the dependency — but the generator matches it by syntactic name, so a local
    /// declaration drives the same branch. The header lookup relies on
    /// <c>HeaderDictionaryExtensions.Get</c>; calling <c>Get</c> on a plain dictionary without it is
    /// a defect that shipped once already.
    /// </summary>
    [Fact]
    public void AFromContextParameterBindsFromTheNamedHeader() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process([FromContext("awsRequestId")] string id) { }
            """, FunctionGeneratorHarness.FromContextAttributeDeclaration))
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        Assert.Contains("context.Request.Headers.Get(\"awsRequestId\")", source);
    }

    /// <summary>
    /// <c>[FromContext]</c> with no name falls back to the parameter's own identifier, the same way
    /// every other named binding attribute does.
    /// </summary>
    [Fact]
    public void AFromContextParameterWithNoNameUsesTheParameterName() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process([FromContext] string awsRequestId) { }
            """, FunctionGeneratorHarness.FromContextAttributeDeclaration))
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        Assert.Contains("context.Request.Headers.Get(\"awsRequestId\")", source);
    }

    /// <summary>
    /// A parameter with a default value is parsed with that default rather than required, so a
    /// missing header leaves the handler with the declared value instead of throwing.
    /// </summary>
    [Fact]
    public void AFromContextParameterWithADefaultIsParsedWithThatDefault() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process([FromContext("retry")] int retry = 3) { }
            """, FunctionGeneratorHarness.FromContextAttributeDeclaration))
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        Assert.Contains("ParseWithDefault", source);
        Assert.DoesNotContain("ParseRequired", source);
    }

    /// <summary>
    /// The parameter metadata array the filter pipeline reads: one entry per parameter, carrying
    /// the declared name, position and type.
    /// </summary>
    [Fact]
    public void ParameterMetadataRecordsEachParameterNamePositionAndType() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process(DataModel model, IThing thing) { }
            """, FunctionGeneratorHarness.SupportTypes))
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        Assert.Contains("new global::Hardened.Requests.Abstract.Execution.IExecutionRequestParameter[2]", source);
        Assert.Contains("\"model\"", source);
        Assert.Contains("typeof(global::TestApp.DataModel)", source);
        Assert.Contains("\"thing\"", source);
        Assert.Contains("typeof(global::TestApp.IThing)", source);
    }

    /// <summary>
    /// A binding attribute other than <c>[FromContext]</c> falls through to the shared custom
    /// attribute handling, which defers the binding to the attribute itself at run time rather than
    /// deciding it here. <c>[FromServices]</c> is the one a function handler is most likely to
    /// carry, on a service that is a class rather than an interface.
    /// </summary>
    [Fact]
    public void AFromServicesParameterBindsThroughTheCustomAttributePath() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process([FromServices] IThing thing) { }
            """, FunctionGeneratorHarness.SupportTypes))
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        Assert.Contains("ExecutionHelper.CustomAttributeData<global::TestApp.IThing>(", source);
    }

    /// <summary>
    /// <c>[FromBody]</c> on a function parameter takes the same custom-attribute route. Worth
    /// pinning because the payload already binds from the body without it — writing it explicitly
    /// must not produce something different.
    /// </summary>
    [Fact]
    public void AnExplicitFromBodyParameterBindsThroughTheCustomAttributePath() {
        var source = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process([FromBody] DataModel model) { }
            """, FunctionGeneratorHarness.SupportTypes))
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        Assert.Contains("ExecutionHelper.CustomAttributeData<global::TestApp.DataModel>(", source);
    }

    /// <summary>
    /// A nullable payload. The generated Parameters property has to be declared nullable too, or
    /// the assignment from a nullable deserialise result warns — and CI treats warnings as errors.
    /// </summary>
    [Fact]
    public void ANullablePayloadParameterCompiles() {
        FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction]
                public void Process(DataModel? model) { }
            """, FunctionGeneratorHarness.SupportTypes)).AssertNoErrors();
    }
}
