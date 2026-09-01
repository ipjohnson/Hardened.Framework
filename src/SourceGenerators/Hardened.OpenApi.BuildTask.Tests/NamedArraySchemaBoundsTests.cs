using System.Linq;
using System.Threading;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Item-count bounds on a schema the contract names at the top level.
/// </summary>
/// <remarks>
/// A property's <c>minItems</c> lands on <c>PropertyModel</c> and always did. A named array
/// schema's landed nowhere - <c>SchemaModel</c> had no field for it - so
/// <c>Batch: { type: array, minItems: 1 }</c> parsed to an unbounded list and the published
/// document said nothing either.
/// </remarks>
public class NamedArraySchemaBoundsTests {

    private const string Document = """
        openapi: 3.0.0
        info: { title: Depot, version: '1.0' }
        paths:
          /batches:
            post:
              operationId: submitBatch
              requestBody:
                content:
                  application/json:
                    schema:
                      $ref: '#/components/schemas/Batch'
              responses:
                '204': { description: Accepted }
        components:
          schemas:
            Batch:
              type: array
              minItems: 1
              maxItems: 100
              items:
                type: string
        """;

    [Fact]
    public void TheBoundsLandOnTheSchemaModel() {
        var model = OpenApiSpecParser.Parse(Document, "depot", CancellationToken.None);

        Assert.NotNull(model);

        var batch = model!.Schemas.Single(schema => schema.Name == "Batch");

        Assert.Equal(SchemaKind.Array, batch.Kind);
        Assert.Equal(1, batch.MinItems);
        Assert.Equal(100, batch.MaxItems);
    }
}
