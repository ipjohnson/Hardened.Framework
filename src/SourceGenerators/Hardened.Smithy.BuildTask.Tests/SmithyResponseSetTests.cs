using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// What a Smithy operation records about the response it succeeds with.
/// </summary>
/// <remarks>
/// <para>
/// Smithy models one output per operation, so there is never the multiple 2xx a description can
/// declare - but the emitters read <c>SuccessResponses</c> rather than the flat fields when they
/// build a response set, and this parser did not fill it. An operation answering 204 therefore
/// produced a set carrying only its errors and no case a handler could return to say it had
/// succeeded, and the generated interface named a type nothing emitted.
/// </para>
/// <para>
/// The OpenAPI parser was changed and this one was not, which is the shape of omission the two
/// front ends invite: they share the model and the emitters, and nothing compares what each puts
/// into it.
/// </para>
/// </remarks>
public class SmithyResponseSetTests {

    /// <summary>
    /// A CLI-built AST, like every other fixture here - this parser reads the JSON the Smithy CLI
    /// produces rather than .smithy source, which is also why a version mismatch fails the build.
    /// </summary>
    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response-set.json"));

    private static OperationModel Operation(string operationId) {
        var model = SmithySpecParser.Parse(Fixture(), "response-set", new List<string>());

        Assert.NotNull(model);

        return model!.Services
            .SelectMany(service => service.Operations)
            .Single(operation => operation.OperationId == operationId);
    }

    [Fact]
    public void Parse_RecordsTheOperationsSuccessAsADeclaredResponse() {
        var success = Assert.Single(Operation("GetTodo").SuccessResponses);

        Assert.Equal(200, success.StatusCode);
        Assert.NotNull(success.Ref);
    }

    /// <summary>
    /// The 204, which is the case that had nowhere to go. Its status is carried and its body is
    /// null, which is what makes the emitted case a bodyless one rather than an absent one.
    /// </summary>
    [Fact]
    public void Parse_RecordsABodylessSuccess() {
        var success = Assert.Single(Operation("RemoveTodo").SuccessResponses);

        Assert.Equal(204, success.StatusCode);
        Assert.Null(success.Ref);
    }

    /// <summary>
    /// The status the @http trait names, on both the flat field every existing consumer reads and
    /// the list the response set is built from. Two places, one answer.
    /// </summary>
    [Fact]
    public void Parse_AgreesWithTheFlatSuccessStatus() {
        var operation = Operation("RemoveTodo");

        Assert.Equal(operation.SuccessStatusCode, Assert.Single(operation.SuccessResponses).StatusCode);
    }
}
