using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// A document promising rendered markup for a model, and an implementation with nothing to render
/// it.
/// </summary>
/// <remarks>
/// <para>
/// This is what naming the view on the implementation buys back. Moving it out of the document
/// removed a spec extension, and the obvious cost would be that the two can now disagree - but they
/// compile in the same pass, so a disagreement is a build error rather than a 500 on the first
/// request. Nothing serializes an object as <c>text/html</c>, so before this the response fell
/// through to JSON or to no serializer at all.
/// </para>
/// </remarks>
public class MissingTemplateTests {
    private const string DiagnosticId = "HOAG020";

    private static string MarkupFor(string schema) =>
        $$"""
        openapi: "3.0.0"
        info: { title: Views, version: "1.0" }
        paths:
          /fortunes:
            get:
              tags: [Fortune]
              operationId: fortunes
              responses:
                '200':
                  description: The fortunes table
                  content:
                    text/html:
                      schema: {{schema}}
        components:
          schemas:
            FortunePage:
              type: object
              properties:
                message: { type: string }
        """;

    private static bool Reports(string spec) =>
        OpenApiGenerator.Run(spec).GeneratorDiagnostics.Any(diagnostic => diagnostic.Id == DiagnosticId);

    /// <summary>
    /// An operation answering markup with a model needs a view, and the implementation here names
    /// none - it is not written at all, so nothing could have named one.
    /// </summary>
    [Fact]
    public void MarkupForAModelWithNoViewIsAnError() {
        Assert.True(Reports(MarkupFor("{ $ref: '#/components/schemas/FortunePage' }")));
    }

    /// <summary>
    /// A list of models is the same case. Serializing one as markup is no more possible than
    /// serializing a single one.
    /// </summary>
    [Fact]
    public void MarkupForAListOfModelsIsAlsoAnError() {
        Assert.True(Reports(MarkupFor(
            "{ type: array, items: { $ref: '#/components/schemas/FortunePage' } }")));
    }

    /// <summary>
    /// A handler returning a string builds its own markup, which is a legitimate thing to write and
    /// needs no view. Reporting it would make the diagnostic something to work around.
    /// </summary>
    [Fact]
    public void MarkupForAStringIsNotReported() {
        Assert.False(Reports(MarkupFor("{ type: string }")));
    }

    /// <summary>And JSON for a model is the ordinary case.</summary>
    [Fact]
    public void JsonForAModelIsNotReported() {
        Assert.False(Reports(
            """
            openapi: "3.0.0"
            info: { title: Views, version: "1.0" }
            paths:
              /fortunes:
                get:
                  tags: [Fortune]
                  operationId: fortunes
                  responses:
                    '200':
                      description: The fortunes table
                      content:
                        application/json:
                          schema: { $ref: '#/components/schemas/FortunePage' }
            components:
              schemas:
                FortunePage:
                  type: object
                  properties:
                    message: { type: string }
            """));
    }
}
