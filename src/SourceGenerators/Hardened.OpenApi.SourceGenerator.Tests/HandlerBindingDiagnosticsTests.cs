using System.Collections.Generic;
using CSharpAuthor;
using Hardened.Idl.SourceGenerator;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The three ways a described operation ends up doing less than its implementation says, with a
/// clean build.
/// </summary>
/// <remarks>
/// All three were silent. A missing <c>[Handler]</c> produced routes that existed and failed at
/// request time; a handler whose base list started with a base class was registered against that
/// class, so the service resolved to nothing; and a declaration the described path never reads
/// compiled on the implementation and changed nothing.
/// </remarks>
public class HandlerBindingDiagnosticsTests {

    private static RequestHandlerModel Operation(string serviceName, string path) =>
        new(new RequestHandlerNameModel(path, "GET"),
            TypeDefinition.Get("Test.Api.Services", serviceName),
            "Invoke",
            TypeDefinition.Get("Test.Api.Generated", serviceName + "_Invoke"),
            Array.Empty<RequestParameterInformation>(),
            new ResponseInformationModel { IsAsync = true },
            Array.Empty<AttributeModel>());

    private static HandlerInfo Handler(string implementation, params string[] baseList) =>
        new(TypeDefinition.Get("Test.Api", implementation),
            Array.ConvertAll(baseList, name => (ITypeDefinition)TypeDefinition.Get("Test.Api", name)),
            Array.Empty<AttributeModel>(),
            Array.Empty<HandlerMethodFilterInfo>());

    private static IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> Report(
        IReadOnlyList<RequestHandlerModel> models, IReadOnlyList<HandlerInfo> handlers) =>
        HandlerBindingDiagnostics.Collect(models, handlers);

    [Fact]
    public void ADescribedServiceWithNoHandlerIsReported() {
        var diagnostics = Report(
            [Operation("IPetService", "/pets")],
            []);

        var diagnostic = Assert.Single(diagnostics);

        Assert.Equal(HandlerBindingDiagnostics.NoHandlerId, diagnostic.Id);
        Assert.Contains("IPetService", diagnostic.GetMessage());
    }

    /// <summary>The count is in the message, because one missing handler can kill many routes.</summary>
    [Fact]
    public void TheReportNamesHowManyRoutesTheMissingHandlerCosts() {
        var diagnostics = Report(
            [Operation("IPetService", "/pets"), Operation("IPetService", "/pets/{id}")],
            []);

        Assert.Contains("2 route", Assert.Single(diagnostics).GetMessage());
    }

    [Fact]
    public void AnImplementedServiceIsNotReported() {
        var diagnostics = Report(
            [Operation("IPetService", "/pets")],
            [Handler("PetServiceImpl", "IPetService")]);

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// The case the base-list fix is for: the interface is present but not first.
    /// </summary>
    [Fact]
    public void AServiceImplementedAfterABaseClassIsNotReported() {
        var diagnostics = Report(
            [Operation("IPetService", "/pets")],
            [Handler("PetServiceImpl", "HandlerBase", "IPetService")]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AHandlerNamingNoDescribedServiceIsReported() {
        var diagnostics = Report(
            [Operation("IPetService", "/pets")],
            [Handler("StrayImpl", "HandlerBase", "IDisposable")]);

        Assert.Contains(diagnostics, d => d.Id == HandlerBindingDiagnostics.NoServiceInterfaceId);
    }

    /// <summary>The message lists what it did find, so the mismatch is readable without a rebuild.</summary>
    [Fact]
    public void TheStrayHandlerReportListsItsBaseTypes() {
        var diagnostics = Report(
            [Operation("IPetService", "/pets")],
            [Handler("StrayImpl", "HandlerBase", "IDisposable")]);

        var stray = Assert.Single(
            diagnostics, d => d.Id == HandlerBindingDiagnostics.NoServiceInterfaceId);

        Assert.Contains("HandlerBase", stray.GetMessage());
        Assert.Contains("IDisposable", stray.GetMessage());
    }

    /// <summary>
    /// A project with no description at all says nothing. Hand-written <c>[Handler]</c> classes
    /// belong to the other generator, and reporting them here would fire on every web application.
    /// </summary>
    [Fact]
    public void NothingIsReportedWhenTheProjectDescribesNoServices() {
        var diagnostics = Report([], [Handler("SomeImpl", "ISomething")]);

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Both are warnings. A package shipping the generated interfaces for a client to implement is a
    /// supported target, so an error would make it unbuildable; the escape hatch is NoWarn.
    /// </summary>
    [Fact]
    public void BothAreWarningsRatherThanErrors() {
        var diagnostics = Report(
            [Operation("IPetService", "/pets")],
            [Handler("StrayImpl", "IDisposable")]);

        Assert.All(diagnostics, d =>
            Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, d.Severity));
    }

    private static HandlerInfo HandlerDeclaring(string method, string attributeName) =>
        new(TypeDefinition.Get("Test.Api", "PetServiceImpl"),
            new[] { (ITypeDefinition)TypeDefinition.Get("Test.Api", "IPetService") },
            Array.Empty<AttributeModel>(),
            new[] {
                new HandlerMethodFilterInfo(
                    method,
                    new[] {
                        new AttributeModel(
                            TypeDefinition.Get("Hardened.Requests.Abstract.Attributes", attributeName),
                            "", "")
                    })
            });

    /// <summary>
    /// <c>[RawResponse]</c> on a described handler, which the generator reads from a handler's own
    /// syntax and a described operation does not have.
    /// </summary>
    /// <remarks>
    /// It compiled, read as a commitment to a content type in review, and did nothing at all - the
    /// described path takes the media type from the contract.
    /// </remarks>
    [Fact]
    public void ADeclarationTheDescribedPathDoesNotReadIsReported() {
        var diagnostics = Report(
            new[] { Operation("IPetService", "/pets") },
            new[] { HandlerDeclaring("ListPets", "RawResponseAttribute") });

        var diagnostic = Assert.Single(diagnostics);

        Assert.Equal(HandlerBindingDiagnostics.InertDeclarationId, diagnostic.Id);

        var message = diagnostic.GetMessage();

        Assert.Contains("PetServiceImpl.ListPets", message);
        Assert.Contains("[RawResponse]", message);
        Assert.Contains("media type", message);
    }

    /// <summary>
    /// A warning, so a project can silence it. The described path ignoring the attribute is not a
    /// reason to refuse to build an otherwise correct application.
    /// </summary>
    [Fact]
    public void TheInertDeclarationIsAWarning() =>
        Assert.Equal(
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
            Assert.Single(Report(
                new[] { Operation("IPetService", "/pets") },
                new[] { HandlerDeclaring("ListPets", "RawResponseAttribute") })).Severity);

    /// <summary>
    /// A declaration the described path does read is left alone. Without this the rule could pass
    /// its other tests by firing on every attribute.
    /// </summary>
    [Fact]
    public void ADeclarationTheDescribedPathReadsIsNotReported() =>
        Assert.Empty(Report(
            new[] { Operation("IPetService", "/pets") },
            new[] { HandlerDeclaring("ListPets", "RateLimitAttribute") }));
}
