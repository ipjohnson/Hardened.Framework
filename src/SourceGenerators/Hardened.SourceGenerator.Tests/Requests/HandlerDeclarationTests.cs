using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Hardened.SourceGenerator.Web;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// Findings decided on the handler model and reported where the handler is written, so the build
/// prints a file and a line rather than <c>CSC :</c>, and an editor opens it there.
/// </summary>
public class HandlerDeclarationTests
{
    private static Diagnostic Reported(string id, string handlers) =>
        Assert.Single(
            RequestGeneratorHarness
                .Generate(
                    RequestGeneratorHarness
                        .Controller(handlers)
                        .Replace(
                            "using System;",
                            "using System;\nusing Hardened.Requests.Abstract.Forms;"
                        )
                )
                .GeneratorDiagnostics,
            diagnostic => diagnostic.Id == id
        );

    /// <summary>The text the location covers, which is what an editor underlines.</summary>
    private static string Underlined(Diagnostic diagnostic) =>
        diagnostic
            .Location.SourceTree!.GetText(TestContext.Current.CancellationToken)
            .ToString(diagnostic.Location.SourceSpan);

    [Fact]
    public void AMisboundFileIsReportedAtItsParameter()
    {
        var reported = Reported(
            FormFileDiagnostics.DiagnosticId,
            """
                [Post("/photos")]
                public string AddPhoto(string caption, IFormFile photo) => caption;
            """
        );

        Assert.Equal(LocationKind.SourceFile, reported.Location.Kind);
        Assert.Equal("photo", Underlined(reported));
    }

    [Fact]
    public void BytesWithNoDeclaredMediaTypeAreReportedAtTheHandlersName()
    {
        var reported = Reported(
            ContentTypeDiagnostics.MissingDeclarationId,
            """
                [Get("/photo")]
                public byte[] GetPhoto() => new byte[0];
            """
        );

        Assert.Equal("GetPhoto", Underlined(reported));
    }

    /// <summary>
    /// Two methods of one name, told apart by their parameters, so the finding is reported at the
    /// one it is about.
    /// </summary>
    [Fact]
    public void AnOverloadIsReportedAtTheOneTheFindingIsAbout()
    {
        var reported = Reported(
            ContentTypeDiagnostics.MissingDeclarationId,
            """
                [Get("/photo/{id}")]
                [Produces("image/png")]
                public byte[] Photo(string id) => new byte[0];

                [Get("/photo/{id}/{size}")]
                public byte[] Photo(string id, int size) => new byte[0];
            """
        );

        var line = reported.Location.GetLineSpan().StartLinePosition.Line;

        Assert.Contains(
            "int size",
            reported
                .Location.SourceTree!.GetText(TestContext.Current.CancellationToken)
                .Lines[line]
                .ToString()
        );
    }
}
