using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// The reader for the report the Smithy CLI prints on standard error.
/// </summary>
/// <remarks>
/// The fixtures are the verbatim output of the pinned CLI (1.73.0) over small deliberately broken
/// models, not hand-written approximations - the parser exists to read exactly what that version
/// prints, and a fixture that drifts from it would prove nothing. Anything the parser does not
/// recognise must yield no findings, because the caller's fallback is to pass the text through
/// whole rather than lose it.
/// </remarks>
public class SmithyValidationReportTests {

    /// <summary>
    /// <c>smithy ast</c> over a service naming an operation that does not exist: one ERROR with a
    /// shape, one DANGER without, and the FAILURE count line.
    /// </summary>
    private const string FailureReport =
        """

        ──  ERROR  ────────────────────────────────────────────── Target.UnresolvedShape
        Shape: probe#Svc
        File:  bad.smithy:4:1

        4| service Svc {
         | ^

        service shape has an `operation` relationship to an unresolved shape
        `probe#MissingOp`


        ──  DANGER  ───────────────────────────────────────────── SyntacticShapeIdTarget
        File:  bad.smithy:6:18

        4| service Svc {
        5|     version: "1"
        6|     operations: [MissingOp]
         |                  ^

        Syntactic shape ID `MissingOp` does not resolve to a valid shape ID:
        `probe#MissingOp`. Did you mean to quote this string? Are you missing a model
        file?

        FAILURE: Validated 242 shapes (ERROR: 1, DANGER: 1)
        """;

    [Fact]
    public void EveryBannerBecomesOneFinding() {
        Assert.Equal(2, SmithyValidationReport.Parse(FailureReport).Count);
    }

    [Fact]
    public void TheSeverityAndEventIdComeFromTheBannerLine() {
        var findings = SmithyValidationReport.Parse(FailureReport);

        Assert.Equal("ERROR", findings[0].Severity);
        Assert.Equal("Target.UnresolvedShape", findings[0].Id);
        Assert.Equal("DANGER", findings[1].Severity);
        Assert.Equal("SyntacticShapeIdTarget", findings[1].Id);
    }

    /// <summary>ERROR and DANGER are the two severities the CLI fails validation on.</summary>
    [Fact]
    public void BothFailingSeveritiesSaySo() {
        Assert.All(
            SmithyValidationReport.Parse(FailureReport),
            finding => Assert.True(finding.FailedValidation));
    }

    [Fact]
    public void TheLocationComesFromTheFileLine() {
        var finding = SmithyValidationReport.Parse(FailureReport)[1];

        Assert.Equal("bad.smithy", finding.File);
        Assert.Equal(6, finding.Line);
        Assert.Equal(18, finding.Column);
    }

    /// <summary>The Shape line is optional - the DANGER banner above has none.</summary>
    [Fact]
    public void TheShapeIsCarriedWhenNamedAndNullWhenNot() {
        var findings = SmithyValidationReport.Parse(FailureReport);

        Assert.Equal("probe#Svc", findings[0].Shape);
        Assert.Null(findings[1].Shape);
    }

    /// <summary>
    /// The CLI wraps one sentence across lines at its banner width; the message is reassembled
    /// without the source excerpt, whose content the file and line already point an editor at.
    /// </summary>
    [Fact]
    public void TheMessageIsReassembledWithoutTheExcerpt() {
        var finding = SmithyValidationReport.Parse(FailureReport)[1];

        Assert.Equal(
            "Syntactic shape ID `MissingOp` does not resolve to a valid shape ID: " +
            "`probe#MissingOp`. Did you mean to quote this string? Are you missing a model file?",
            finding.Message);
    }

    /// <summary>Its content is the finding count, which the findings already carry.</summary>
    [Fact]
    public void TheSummaryLineIsNotPartOfAnyMessage() {
        Assert.All(
            SmithyValidationReport.Parse(FailureReport),
            finding => Assert.DoesNotContain("FAILURE", finding.Message));
    }

    /// <summary>
    /// A Windows path carries a colon of its own, so the line and column are read from the right.
    /// </summary>
    [Fact]
    public void AWindowsPathKeepsItsDriveColon() {
        var finding = SmithyValidationReport.Parse(
            "──  ERROR  ──── Model.Broken\n" +
            "File:  C:\\models\\bad.smithy:12:3\n" +
            "\n" +
            "the message\n").Single();

        Assert.Equal("C:\\models\\bad.smithy", finding.File);
        Assert.Equal(12, finding.Line);
        Assert.Equal(3, finding.Column);
    }

    /// <summary>A banner with no File line still parses; the caller falls back to the model.</summary>
    [Fact]
    public void ABannerWithoutAFileLineYieldsAnEmptyFile() {
        var finding = SmithyValidationReport.Parse(
            "──  WARNING  ──── SomethingGeneral\n\nthe whole model is suspect\n").Single();

        Assert.Equal("", finding.File);
        Assert.Equal(0, finding.Line);
        Assert.False(finding.FailedValidation);
    }

    /// <summary>
    /// Anything that is not the report - a Java stacktrace, a launcher complaint - must yield
    /// nothing, so the caller passes it through whole instead of losing it.
    /// </summary>
    [Fact]
    public void TextThatIsNotTheReportYieldsNoFindings() {
        Assert.Empty(SmithyValidationReport.Parse(
            "Exception in thread \"main\" java.lang.OutOfMemoryError: Java heap space\n" +
            "\tat software.amazon.smithy.cli.SmithyCli.run(SmithyCli.java:80)\n"));
        Assert.Empty(SmithyValidationReport.Parse(""));
    }

    /// <summary>Carriage returns are the launcher's on Windows, not content.</summary>
    [Fact]
    public void CarriageReturnsAreTolerated() {
        var finding = SmithyValidationReport.Parse(
            "──  ERROR  ──── Model.Broken\r\nFile:  bad.smithy:2:1\r\n\r\nthe message\r\n").Single();

        Assert.Equal("bad.smithy", finding.File);
        Assert.Equal("the message", finding.Message);
    }
}
