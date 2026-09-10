using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// The array a routing table carries for the filters its entry point declares.
/// </summary>
/// <remarks>
/// <para>
/// One array beside the table rather than a copy in every handler's metadata: a handler model is a
/// Roslyn cache key, so folding the entry point's attributes into each one would make an edit to
/// the module class invalidate every handler in the application, and the arguments would be spelled
/// once per handler in the emitted assembly.
/// </para>
/// <para>
/// Asserted on the emitted C# because that is where the two halves of this meet - the nested class
/// and the registration that hands it to <c>ExecutionHelper</c> - and a table that emits one
/// without the other compiles and installs nothing.
/// </para>
/// </remarks>
public class ApplicationFilterEmitterTests {

    private static AttributeModel Declaration(
        string @namespace, string name, string arguments = "", string properties = "") =>
        new(TypeDefinition.Get(@namespace, name), arguments, properties);

    private static EntryPointSelector.Model App(params AttributeModel[] filters) =>
        new() {
            EntryPointType = TypeDefinition.Get("Test.Api", "TestApp"),
            AttributeModels = Array.Empty<AttributeModel>(),
            RootEntryPoint = true,
            MethodDefinitions = Array.Empty<HardenedMethodDefinition>(),
            FilterDeclarations = filters
        };

    private static RequestHandlerModel Handler() =>
        new(new RequestHandlerNameModel("/books", "GET"),
            TypeDefinition.Get("Test.Api.Services", "IBookService"),
            "List",
            TypeDefinition.Get("Test.Api.Generated", "BookController_List"),
            Array.Empty<RequestParameterInformation>(),
            new ResponseInformationModel { IsAsync = true },
            Array.Empty<AttributeModel>());

    private static string Emit(params AttributeModel[] filters) =>
        RoutingTableGenerator.GenerateCSharpRouteFile(
            App(filters), [Handler()], CancellationToken.None);

    [Fact]
    public void ADeclaredFilterIsConstructedOnceAndRegistered() {
        var generated = Emit(Declaration("Hardened.Web.Runtime.Conditional", "ConditionalGetAttribute"));

        Assert.Contains("class ApplicationFilters", generated);
        Assert.Contains("new ConditionalGetAttribute()", generated);
        Assert.Contains("IApplicationFilterDeclarations", generated);
        Assert.Contains("TestApp.ApplicationFilters", generated);
    }

    /// <summary>
    /// With whatever it was written with. The array is the one place an application-wide
    /// declaration's arguments are spelled.
    /// </summary>
    [Fact]
    public void TheArgumentsAndInitializersAreEmittedWithIt() {
        var generated = Emit(
            Declaration("Test.Api.Filters", "AuditAttribute", "\"quotes\"", "Level = 2"));

        Assert.Contains("new AuditAttribute(\"quotes\"){ Level = 2 }", generated);
    }

    /// <summary>
    /// Several declarations keep the order they were written in, which is the order they are merged
    /// into a handler's metadata and therefore the order they break ties in.
    /// </summary>
    [Fact]
    public void SeveralDeclarationsKeepTheOrderTheyWereWrittenIn() {
        var generated = Emit(
            Declaration("Hardened.Requests.Runtime.Filters", "RetryAttribute"),
            Declaration("Hardened.Web.Runtime.Conditional", "ConditionalGetAttribute"));

        Assert.True(
            generated.IndexOf("new RetryAttribute()", StringComparison.Ordinal) <
            generated.IndexOf("new ConditionalGetAttribute()", StringComparison.Ordinal),
            "The emitted array should read in declaration order.");
    }

    /// <summary>
    /// An entry point declaring no filter emits neither the class nor the registration, so a table
    /// that does not use this generates exactly what it generated before it existed - which is what
    /// keeps the checked-in routing fixtures unmoved.
    /// </summary>
    [Fact]
    public void AnEntryPointDeclaringNoFilterEmitsNothing() {
        var generated = Emit();

        Assert.DoesNotContain("ApplicationFilters", generated);
        Assert.DoesNotContain("IApplicationFilterDeclarations", generated);
    }
}
