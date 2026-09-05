using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Web.Testing.Tests.Transport;

/// <summary>
/// Which host a test runs on: the narrowest declaration in scope, whether it is a runtime
/// attribute a provider answers for or an explicit host attribute, and the pipeline with none.
/// </summary>
public class HostResolutionTests {

    /// <summary>Stands in for a runtime attribute an application names its host with.</summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly)]
    private sealed class SomeRuntimeAttribute : Attribute { }

    /// <summary>A runtime attribute no provider in scope answers for.</summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly)]
    private sealed class OtherRuntimeAttribute : Attribute { }

    private sealed class SomeHost : ITestHost {
        public bool IsTerminal => true;

        public Uri BaseAddress => new("http://some/");

        public Task StartAsync(IServiceProvider provider, CancellationToken cancellationToken) => Task.CompletedTask;

        public HttpMessageHandler CreateHandler(TestCredential? credential) => throw new NotSupportedException();

        public Task<TestWebResponse> SendAsync(TestHostRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => default;
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    private sealed class SomeTestingAttribute : TestHostProviderAttribute {
        public override Type RuntimeAttribute => typeof(SomeRuntimeAttribute);

        public override ITestHost CreateHost(ITestMethodContext testMethod, IServiceCollection services) => new SomeHost();
    }

    private sealed class Context : ITestMethodContext {
        public Context(params Attribute[] widestFirst) {
            Attributes = widestFirst;
        }

        public MethodInfo Method { get; } = typeof(Context).GetMethod(nameof(Marker), BindingFlags.NonPublic | BindingFlags.Static)!;

        public IReadOnlyList<Attribute> Attributes { get; }

        private static void Marker() { }
    }

    private static ITestHost Resolve(params Attribute[] widestFirst) =>
        WebTestingAttribute.ResolveHost(new Context(widestFirst), new ServiceCollection());

    [Fact]
    public void WithNothingInScopeThePipelineServes() {
        Assert.IsType<PipelineHost>(Resolve());
    }

    [Fact]
    public void ARuntimeAttributeAProviderAnswersForIsThatHost() {
        Assert.IsType<SomeHost>(Resolve(new SomeTestingAttribute(), new SomeRuntimeAttribute()));
    }

    /// <summary>The module still loads, as the runner always did; the host is the pipeline.</summary>
    [Fact]
    public void ARuntimeAttributeNoProviderAnswersForStaysOnThePipeline() {
        Assert.IsType<PipelineHost>(Resolve(new SomeTestingAttribute(), new OtherRuntimeAttribute()));
    }

    /// <summary>Widest first in the list, so the method's attribute is last and wins.</summary>
    [Fact]
    public void TheNarrowestDeclarationWinsAcrossBothKinds() {
        Assert.IsType<PipelineHost>(Resolve(new SomeTestingAttribute(), new SomeRuntimeAttribute(), new PipelineHostAttribute()));
        Assert.IsType<SomeHost>(Resolve(new SomeTestingAttribute(), new PipelineHostAttribute(), new SomeRuntimeAttribute()));
    }

    [Fact]
    public void AnExplicitHostAttributeIsItsHost() {
        Assert.IsType<PipelineHost>(Resolve(new SomeTestingAttribute(), new PipelineHostAttribute()));
    }
}
