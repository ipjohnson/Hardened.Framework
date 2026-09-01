using System.Linq;
using System.Threading;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The set an operation's responses are negotiated against, collected from every declared
/// response rather than the successes alone.
/// </summary>
/// <remarks>
/// Collected from the 2xx loop only, a <c>text/plain</c> operation declared a set no error model
/// could travel as, so its declared 404 and the framework's own 400 reached the caller as an
/// empty 500. The error representations belong in the set - after the success ones, because the
/// set is negotiated first-match for a client that states no preference and the success
/// representation must stay the one such a client gets.
/// </remarks>
public class ProducedContentTypesTests {

    private const string PlainTextWithErrors = """
        openapi: 3.0.0
        info: { title: Labels, version: 1.0.0 }
        paths:
          /labels/{id}:
            get:
              operationId: getLabel
              parameters:
                - { name: id, in: path, required: true, schema: { type: string } }
              responses:
                '200':
                  description: The label
                  content: { text/plain: { schema: { type: string } } }
                '404':
                  description: No such label
                  content: { application/json: { schema: { $ref: '#/components/schemas/Problem' } } }
                '429':
                  description: Slow down
                  content: { application/json: { schema: { $ref: '#/components/schemas/Problem' } } }
          /labels/{id}/status:
            get:
              operationId: getLabelStatus
              parameters:
                - { name: id, in: path, required: true, schema: { type: string } }
              responses:
                '200':
                  description: The status
                  content: { application/json: { schema: { $ref: '#/components/schemas/Problem' } } }
                '404':
                  description: No such label, no body declared for it
          /labels/{id}/archive:
            post:
              operationId: archiveLabel
              parameters:
                - { name: id, in: path, required: true, schema: { type: string } }
              responses:
                '204': { description: Archived }
                '404':
                  description: No such label
                  content: { application/json: { schema: { $ref: '#/components/schemas/Problem' } } }
        components:
          schemas:
            Problem: { type: object, properties: { detail: { type: string } } }
        """;

    private static OperationModel Operation(string operationId) {
        var model = OpenApiSpecParser.Parse(PlainTextWithErrors, "labels", CancellationToken.None);

        Assert.NotNull(model);

        return model!.Services.SelectMany(service => service.Operations)
            .Single(operation => operation.OperationId == operationId);
    }

    [Fact]
    public void TheErrorRepresentationsFollowTheSuccessOnes() {
        Assert.Equal(
            new[] { "text/plain", "application/json" },
            Operation("getLabel").ProducedContentTypes);
    }

    [Fact]
    public void ARepresentationSharedBySuccessAndErrorAppearsOnce() {
        Assert.Equal(
            new[] { "application/json" },
            Operation("getLabelStatus").ProducedContentTypes);
    }

    /// <summary>
    /// A bodiless success contributes nothing, so the set is the error representations alone -
    /// which are the only bodies this operation ever writes.
    /// </summary>
    [Fact]
    public void ABodilessSuccessLeavesTheErrorRepresentations() {
        Assert.Equal(
            new[] { "application/json" },
            Operation("archiveLabel").ProducedContentTypes);
    }
}
