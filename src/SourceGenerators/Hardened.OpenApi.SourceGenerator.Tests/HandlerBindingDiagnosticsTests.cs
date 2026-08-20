using System.Collections.Generic;
using CSharpAuthor;
using Hardened.Idl.SourceGenerator;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The two ways a described operation ends up with nothing behind it and a clean build.
/// </summary>
/// <remarks>
/// Both were silent. A missing <c>[Handler]</c> produced routes that existed and failed at request
/// time; a handler whose base list started with a base class was registered against that class, so
/// the service resolved to nothing. Neither produced a diagnostic of any kind.
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
}
