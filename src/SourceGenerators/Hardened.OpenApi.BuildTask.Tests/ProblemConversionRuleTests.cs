using System.Collections.Generic;
using Hardened.Generation;
using Hardened.Generation.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The conversion a response set offers from a bare shipped record - what
/// <c>return new NotFound("todo", "...")</c> becomes - as a rule, before anything is written.
/// </summary>
/// <remarks>
/// <para>
/// The rule is one decision asked twice: by the set, which writes the operator, and by the holder,
/// which writes the method the operator calls. These tests hold it to the cases it must refuse.
/// The emitted text is <c>ProblemConversionEmitterTests</c>' subject.
/// </para>
/// <para>
/// Linked into the Roslyn generator's test project as well, because that generator compiles the
/// shared rules in as source and the rule has to hold there too.
/// </para>
/// </remarks>
public class ProblemConversionRuleTests {



    private static SchemaModel Problem(string name = "Problem", bool withDetail = true) {
        var schema = new SchemaModel { Name = name, Kind = SchemaKind.Object };

        schema.Properties.Add(new PropertyModel { Name = "type", Type = "string" });
        schema.Properties.Add(new PropertyModel { Name = "title", Type = "string" });
        schema.Properties.Add(new PropertyModel { Name = "status", Type = "integer" });

        if (withDetail) {
            schema.Properties.Add(new PropertyModel { Name = "detail", Type = "string" });
        }

        return schema;
    }

    private static SchemaModel Plain(string name) {
        var schema = new SchemaModel { Name = name, Kind = SchemaKind.Object };

        schema.Properties.Add(new PropertyModel { Name = "message", Type = "string" });

        return schema;
    }

    /// <summary>A Smithy error shape: @error, a message, and whatever else the model requires.</summary>
    private static SchemaModel ErrorShape(string name, params string[] requiredBesideMessage) {
        var schema = new SchemaModel { Name = name, Kind = SchemaKind.Object, IsErrorShape = true };

        schema.Properties.Add(new PropertyModel { Name = "message", Type = "string", IsRequired = true });
        schema.Required.Add("message");

        foreach (var member in requiredBesideMessage) {
            schema.Properties.Add(new PropertyModel { Name = member, Type = "string", IsRequired = true });
            schema.Required.Add(member);
        }

        return schema;
    }

    private static ErrorResponseModel Error(int status, string schema) =>
        new() { StatusCode = status, Ref = "#/components/schemas/" + schema };

    private static ErrorResponseModel Named(int status, string schema, string caseType) =>
        new() { StatusCode = status, Ref = "#/components/schemas/" + schema, Name = schema, TypeName = caseType };



    [Fact]
    public void AProblemShapedBodyOnAShippedRecordConverts() {
        var plan = ProblemConversion.For(Error(404, "Problem"), [Problem()]);

        Assert.NotNull(plan);
        Assert.Equal("NotFound", plan.Value.BareRecord);
        Assert.Equal("NotFound", plan.Value.CaseTypeName);
        Assert.True(plan.Value.CaseIsShipped);
        Assert.Equal("NotFoundProblem", plan.Value.MethodName);
    }

    /// <summary>A body that is not 7807 shaped has no members the record's facts belong in.</summary>
    [Fact]
    public void APlainBodyDoesNot() {
        Assert.Null(ProblemConversion.For(Error(404, "ApiError"), [Plain("ApiError")]));
    }

    /// <summary>An error with no body already has the bare record as its case.</summary>
    [Fact]
    public void ABodylessErrorDoesNot() {
        Assert.Null(ProblemConversion.For(new ErrorResponseModel { StatusCode = 404 }, [Problem()]));
    }

    /// <summary>A declared header is a value no record carries.</summary>
    [Fact]
    public void AnErrorDeclaringAHeaderDoesNot() {
        var error = Error(404, "Problem");
        error.Headers.Add(new ResponseHeaderModel { Name = "X-Reason", ParameterName = "reason" });

        Assert.Null(ProblemConversion.For(error, [Problem()]));
    }

    /// <summary>A status the framework ships no record for has nothing to convert from.</summary>
    [Fact]
    public void AStatusWithNoShippedRecordDoesNot() {
        Assert.Null(ProblemConversion.For(Error(423, "Problem"), [Problem()]));
    }

    [Fact]
    public void ASmithyErrorShapeWithAMessageConverts() {
        var plan = ProblemConversion.For(
            Named(404, "TodoNotFound", "TodoNotFoundError"), [ErrorShape("TodoNotFound")]);

        Assert.NotNull(plan);
        Assert.Equal("NotFound", plan.Value.BareRecord);
        Assert.Equal("TodoNotFoundError", plan.Value.CaseTypeName);
        Assert.False(plan.Value.CaseIsShipped);
    }

    /// <summary>A required member beside the message has no source, so the shape is not filled.</summary>
    [Fact]
    public void ASmithyErrorShapeRequiringMoreThanAMessageDoesNot() {
        Assert.Null(ProblemConversion.For(
            Named(404, "TodoNotFound", "TodoNotFoundError"), [ErrorShape("TodoNotFound", "todoId")]));
    }


    /// <summary>The body's arguments the holder writes, read off the record it is handed.</summary>
    [Fact]
    public void TheArgumentsAreReadOffTheRecord() {
        var plan = ProblemConversion.For(Error(404, "Problem"), [Problem()])!.Value;

        Assert.Equal(
            ["value.Type", "value.Title", "value.Status", "value.Detail"],
            ProblemConversion.Arguments(plan, [Problem()], "value"));
    }

    [Fact]
    public void TheHolderIsNamedForTheFile() {
        Assert.Equal("PetstoreProblems", ProblemConversion.HolderName("petstore"));
    }
}
