using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// The handler class emitted for a route registered with a lambda, driven directly.
/// </summary>
/// <remarks>
/// <para>
/// The delegate is the handler instance rather than a field, because every generated invoke method
/// is static. So the controller type argument is the delegate's own type, the invoke is
/// <c>controller.Invoke(...)</c>, and the only thing the emitter does differently is take the
/// delegate in the constructor and hand it to <c>ExecutionHelper</c>.
/// </para>
/// <para>
/// Driven with a hand-built model for the reason <c>ResponseSetEmitTests</c> gives: every generator
/// compiles these sources in rather than referencing the assembly, so a run through one generator
/// says nothing about another's copy. This file is linked into the test projects of the others.
/// </para>
/// </remarks>
public class DelegateHandlerEmitTests
{
    private static readonly ITypeDefinition Target = new GenericTypeDefinition(
        TypeDefinitionEnum.ClassDefinition,
        "System",
        "Func",
        [TypeDefinition.Get(typeof(int)), TypeDefinition.Get("System.Threading.Tasks", "Task")]
    );

    private static RequestHandlerModel Handler(
        bool isDelegate = true,
        bool withParameters = true
    ) =>
        new(
            new RequestHandlerNameModel("/orders/{id}", "GET"),
            isDelegate ? Target : TypeDefinition.Get("TestApp", "OrderController"),
            isDelegate ? "Invoke" : "Get",
            TypeDefinition.Get("TestApp.Generated", "Registered_Get_1"),
            withParameters
                ?
                [
                    new RequestParameterInformation(
                        TypeDefinition.Get(typeof(int)),
                        "id",
                        true,
                        null,
                        ParameterBindType.Path,
                        "id",
                        0
                    ),
                ]
                : [],
            new ResponseInformationModel { IsAsync = true },
            []
        )
        {
            IsDelegateHandler = isDelegate,
        };

    private static string Emit(RequestHandlerModel handler)
    {
        var file = new CSharpFileDefinition("TestApp.Generated");

        InvokeClassGenerator.GenerateInvokeClass(handler, file, CancellationToken.None);

        var context = new OutputContext();

        file.WriteOutput(context);

        return context.Output();
    }

    /// <remarks>
    /// The delegate's own type, not <c>object</c>. A static handler takes <c>object</c> because
    /// there is nothing to be the argument; here there is, and it is what makes
    /// <c>controller.Invoke</c> compile.
    /// </remarks>
    [Fact]
    public void TheDelegateTypeIsTheControllerTypeArgument()
    {
        Assert.Contains("BaseExecutionHandler<Func<int,Task>>", Emit(Handler()));
    }

    /// <remarks>
    /// Ahead of <c>routePath</c>, which has a default. An optional parameter cannot precede a
    /// required one, and the emitted constructor did not compile until this was the order.
    /// </remarks>
    [Fact]
    public void TheConstructorTakesTheDelegateBeforeThePath()
    {
        Assert.Contains(
            "Registered_Get_1(IServiceProvider serviceProvider, Func<int,Task> target, string? routePath = null)",
            Emit(Handler())
        );
    }

    /// <remarks>
    /// Named rather than positional: two of the six <c>ExecutionHelper</c> overloads take a framing
    /// between the filter list and this.
    /// </remarks>
    [Fact]
    public void TheDelegateIsHandedOverAsTheHandlerInstance()
    {
        Assert.Contains("handlerInstance: target", Emit(Handler()));
    }

    [Fact]
    public void TheInvokeCallsTheDelegate()
    {
        Assert.Contains("controller.Invoke(parameters.id)", Emit(Handler()));
    }

    [Fact]
    public void AHandlerWithNoParametersTakesTheDelegateToo()
    {
        var emitted = Emit(Handler(withParameters: false));

        Assert.Contains("handlerInstance: target", emitted);
        Assert.Contains("controller.Invoke()", emitted);
    }

    /// <remarks>
    /// Every handler written as a method is unchanged, which is the property that matters most
    /// here: this mode is additive.
    /// </remarks>
    [Fact]
    public void AnOrdinaryHandlerTakesNoDelegate()
    {
        var emitted = Emit(Handler(isDelegate: false));

        Assert.DoesNotContain("handlerInstance:", emitted);
        Assert.DoesNotContain("target", emitted);
        Assert.Contains("controller.Get(parameters.id)", emitted);
    }
}
