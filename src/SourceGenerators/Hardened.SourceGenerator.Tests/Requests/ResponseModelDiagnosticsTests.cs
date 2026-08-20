using Hardened.SourceGenerator.Requests;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// The build failure a mode this version cannot emit produces.
///
/// <para>
/// The point of these is that they are errors. A declared mode that is accepted and then ignored
/// produces an application which compiles, runs and answers every request the way standard mode
/// does, while its author believes it has a declared response set - and nothing about that looks
/// like a failure. The descriptors are asserted directly rather than through a generator run
/// because they are constructed per call, which is what keeps RS2008 satisfied under
/// <c>EnforceExtendedAnalyzerRules</c>.
/// </para>
/// </summary>
public class ResponseModelDiagnosticsTests {

    [Fact]
    public void TheUnimplementedMode_IsAnError() {
        Assert.Equal(
            DiagnosticSeverity.Error,
            ResponseModelDiagnostics.UnionNotImplementedDescriptor().DefaultSeverity);
    }

    [Fact]
    public void TheDescriptor_CarriesTheDeclaredId() {
        Assert.Equal(
            ResponseModelDiagnostics.UnionNotImplementedId,
            ResponseModelDiagnostics.UnionNotImplementedDescriptor().Id);
    }

    /// <summary>
    /// HRDRM001 was Response mode and is gone, since code-first Response is emitted now. The id is
    /// not reused: a consumer who suppressed it must not silently acquire a suppression for
    /// something else.
    /// </summary>
    [Fact]
    public void TheRetiredResponseId_IsNotReused() {
        Assert.NotEqual("HRDRM001", ResponseModelDiagnostics.UnionNotImplementedId);
    }

    /// <summary>
    /// A reader has to be able to find the module the message is about, and the diagnostic carries
    /// no location - the entry point is known as a type, not as the syntax it was written in.
    /// </summary>
    [Fact]
    public void TheMessage_NamesTheEntryPoint() {
        var message = string.Format(
            ResponseModelDiagnostics.UnionNotImplementedDescriptor().MessageFormat.ToString(),
            "MyApplication");

        Assert.Contains("MyApplication", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The message says what to do instead, and Response is now a mode that works.
    /// </summary>
    [Fact]
    public void TheMessage_NamesAModeThatWorks() {
        var union = ResponseModelDiagnostics.UnionNotImplementedDescriptor()
            .MessageFormat.ToString();

        Assert.Contains("ResponseModel.Response", union, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDescriptor_IsCategorised() {
        Assert.Equal(
            "Hardened.Responses",
            ResponseModelDiagnostics.UnionNotImplementedDescriptor().Category);
    }
}
