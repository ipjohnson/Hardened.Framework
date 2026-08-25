using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hardened.Idl.Emitters;
using Hardened.Generation.Models;
using Hardened.Idl.Filtering;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// An operation that declares more than one 2xx, end to end.
/// </summary>
/// <remarks>
/// <para>
/// The parser used to take the first 2xx and <c>break</c>, so a document declaring 200 and 202
/// parsed as its 200 and the 202 left no trace anywhere - not in the interface, not in the routing,
/// not in the document the build emits back out. It was not a design limit; the set was in hand and
/// one of it was kept.
/// </para>
/// <para>
/// The other half is that a second success cannot be reached by throwing. Standard mode names one
/// success and throws for everything else, and a throw carries a failure - there is no way to throw
/// a 202. So these operations answer with a response set whatever mode the module asked for, which
/// is the one place the module's choice is overridden rather than obeyed.
/// </para>
/// </remarks>
public class MultipleSuccessResponseTests {

    private const string TwoSuccesses = """
        openapi: 3.0.0
        info: { title: Jobs, version: 1.0.0 }
        paths:
          /jobs/{id}:
            get:
              operationId: getJob
              parameters:
                - { name: id, in: path, required: true, schema: { type: string } }
              responses:
                '200':
                  description: Finished
                  content: { application/json: { schema: { $ref: '#/components/schemas/Job' } } }
                '202':
                  description: Still running
                  content: { application/json: { schema: { $ref: '#/components/schemas/JobProgress' } } }
                '404':
                  description: No such job
                  content: { application/json: { schema: { $ref: '#/components/schemas/Problem' } } }
          /jobs/{id}/cancel:
            delete:
              operationId: cancelJob
              parameters:
                - { name: id, in: path, required: true, schema: { type: string } }
              responses:
                '204': { description: Cancelled }
                '404':
                  description: No such job
                  content: { application/json: { schema: { $ref: '#/components/schemas/Problem' } } }
        components:
          schemas:
            Job: { type: object, required: [id], properties: { id: { type: string } } }
            JobProgress: { type: object, required: [percent], properties: { percent: { type: integer } } }
            Problem: { type: object, properties: { detail: { type: string } } }
        """;

    private static ServiceSpecModel Parse() {
        var model = OpenApiSpecParser.Parse(TwoSuccesses, "jobs", CancellationToken.None);

        Assert.NotNull(model);

        return model!;
    }

    private static OperationModel Operation(string operationId) =>
        Parse().Services.SelectMany(service => service.Operations)
            .Single(operation => operation.OperationId == operationId);

    #region parsing

    [Fact]
    public void Parse_CarriesEveryDeclaredSuccess() {
        var operation = Operation("getJob");

        Assert.Equal(new[] { 200, 202 }, operation.SuccessResponses.Select(r => r.StatusCode));
    }

    /// <summary>
    /// The flat fields keep naming the lowest 2xx, so every consumer that reads them is untouched.
    /// </summary>
    [Fact]
    public void Parse_PrimarySuccessIsStillTheLowestStatus() {
        var operation = Operation("getJob");

        Assert.Equal(200, operation.SuccessStatusCode);
        Assert.Equal("#/components/schemas/Job", operation.ResponseRef);
    }

    /// <summary>A 204 is a declared success carrying no body, not an absent one.</summary>
    [Fact]
    public void Parse_ABodylessSuccessIsCarriedWithNoRef() {
        var success = Assert.Single(Operation("cancelJob").SuccessResponses);

        Assert.Equal(204, success.StatusCode);
        Assert.Null(success.Ref);
    }

    #endregion

    #region the signature

    /// <summary>
    /// Standard mode, and still a response set - because the alternative is a document describing a
    /// 202 the handler has no way to produce.
    /// </summary>
    [Fact]
    public void ServiceInterface_MultipleSuccesses_ReturnsAResponseSetEvenInStandardMode() {
        var service = new ServiceModel {
            Tag = "Jobs",
            Operations = new List<OperationModel> { Operation("getJob") }
        };

        var result = EmitterHarness.ServiceInterface(service);

        Assert.Contains("Task<GetJobResponse> GetJob(string id);", result);
    }

    /// <summary>
    /// One success and one error is the case Standard mode was built for, and it is unchanged.
    /// </summary>
    [Fact]
    public void ServiceInterface_OneSuccess_StillThrowsInStandardMode() {
        var service = new ServiceModel {
            Tag = "Jobs",
            Operations = new List<OperationModel> { Operation("cancelJob") }
        };

        var result = EmitterHarness.ServiceInterface(service);

        Assert.Contains("Task CancelJob(string id);", result);
        Assert.DoesNotContain("CancelJobResponse", result);
    }

    #endregion

    #region the emitted cases

    private static string Emit(params OperationModel[] operations) =>
        EmitterHarness.Write(ns => UnionResponseEmitter.Emit(
            ns,
            new ServiceModel { Tag = "jobs", Operations = new List<OperationModel>(operations) },
            EmitterHarness.ModelsNamespace));

    /// <summary>
    /// The primary success is named by its own schema; every other one is wrapped, because the
    /// wrapper is what carries the status.
    /// </summary>
    [Fact]
    public void Emit_WrapsEverySuccessExceptThePrimary() {
        var result = Emit(Operation("getJob"));

        Assert.Contains("public GetJobResponse(Test.Api.Models.Job value)", result);
        Assert.Contains("public GetJobResponse(Test.Api.Models.GetJobAccepted value)", result);
        Assert.Contains("public GetJobResponse(Test.Api.Models.GetJobNotFound value)", result);
    }

    /// <summary>
    /// The 204 case, which is what a bodyless success had no way to express: the union carried the
    /// error and nothing a handler could return to say it had succeeded.
    /// </summary>
    [Fact]
    public void Emit_ABodylessSuccessGetsACaseWithNoBody() {
        var result = Emit(Operation("cancelJob"));

        Assert.Contains("public sealed partial record CancelJobNoContent;", result);
        Assert.Contains("public CancelJobResponse(Test.Api.Models.CancelJobNoContent value)", result);
    }

    /// <summary>
    /// Named for the status, matching the built-in response types a code-first handler returns, so
    /// the same status reads the same in both directions.
    /// </summary>
    [Fact]
    public void Emit_SuccessCasesAreNamedAfterTheirStatus() {
        var result = Emit(Operation("getJob"), Operation("cancelJob"));

        Assert.Contains("GetJobAccepted", result);
        Assert.Contains("CancelJobNoContent", result);
        Assert.DoesNotContain("Status202", result);
        Assert.DoesNotContain("Status204", result);
    }

    #endregion

    /// <summary>
    /// A schema reachable only through a second success survives the unreferenced-schema pass.
    /// </summary>
    /// <remarks>
    /// It did not. The slicer reached the primary success, the array item and every error, so
    /// JobProgress - referenced by the 202 and nothing else - was pruned, and the case type carrying
    /// it named a type nothing declared. CS0234 in generated code, from a document that declares the
    /// schema perfectly well.
    /// </remarks>
    [Fact]
    public void Slice_KeepsASchemaReachedOnlyByANonPrimarySuccess() {
        var model = Parse();

        SpecSlicer.Apply(model, new SpecSlicer.Filter());

        Assert.Contains(model.Schemas, schema => schema.Name == "JobProgress");
    }
}
