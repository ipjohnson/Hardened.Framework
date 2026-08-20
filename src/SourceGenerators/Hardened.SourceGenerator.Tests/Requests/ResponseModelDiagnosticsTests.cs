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
    public void BothUnimplementedModes_AreErrors() {
        Assert.Equal(
            DiagnosticSeverity.Error,
            ResponseModelDiagnostics.ResponseNotImplementedDescriptor().DefaultSeverity);

        Assert.Equal(
            DiagnosticSeverity.Error,
            ResponseModelDiagnostics.UnionNotImplementedDescriptor().DefaultSeverity);
    }

    /// <summary>
    /// Distinct ids, because the two are removed by different pieces of work and a consumer
    /// suppressing one must not lose the other.
    /// </summary>
    [Fact]
    public void TheTwoModes_ReportDistinctIds() {
        Assert.NotEqual(
            ResponseModelDiagnostics.ResponseNotImplementedId,
            ResponseModelDiagnostics.UnionNotImplementedId);

        Assert.Equal(
            ResponseModelDiagnostics.ResponseNotImplementedId,
            ResponseModelDiagnostics.ResponseNotImplementedDescriptor().Id);

        Assert.Equal(
            ResponseModelDiagnostics.UnionNotImplementedId,
            ResponseModelDiagnostics.UnionNotImplementedDescriptor().Id);
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
    /// Each message says what to do instead. Response points at Standard because that is the only
    /// mode that works; Union points at Response, which is what it is for.
    /// </summary>
    [Fact]
    public void EachMessage_NamesAModeThatWorks() {
        var response = ResponseModelDiagnostics.ResponseNotImplementedDescriptor()
            .MessageFormat.ToString();

        var union = ResponseModelDiagnostics.UnionNotImplementedDescriptor()
            .MessageFormat.ToString();

        Assert.Contains("ResponseModel.Standard", response, StringComparison.Ordinal);
        Assert.Contains("ResponseModel.Response", union, StringComparison.Ordinal);
    }

    [Fact]
    public void BothModes_ShareOneCategory() {
        Assert.Equal(
            "Hardened.Responses",
            ResponseModelDiagnostics.ResponseNotImplementedDescriptor().Category);

        Assert.Equal(
            "Hardened.Responses",
            ResponseModelDiagnostics.UnionNotImplementedDescriptor().Category);
    }
}
