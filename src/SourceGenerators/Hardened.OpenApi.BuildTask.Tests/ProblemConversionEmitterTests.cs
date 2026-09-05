using System.Collections.Generic;
using Hardened.Generation;
using Hardened.Generation.Models;
using Hardened.Idl.Emitters;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The holder a response set's conversion from a bare shipped record calls, as written.
/// </summary>
/// <remarks>
/// Which cases convert is <c>ProblemConversionRuleTests</c>' subject; this holds the emitted text
/// to what a handler's return has to compile against, and to the shape that lets a record's
/// <c>Default</c> convert without allocating.
/// </remarks>
public class ProblemConversionEmitterTests {

    private const string Shipped = "global::Hardened.Requests.Abstract.Responses.";

    private const string Models = "global::" + EmitterHarness.ModelsNamespace + ".";

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

    private static string Holder(IReadOnlyList<SchemaModel> schemas, params ErrorResponseModel[] errors) {
        var plans = new List<ProblemConversion.Plan>();

        foreach (var error in errors) {
            if (ProblemConversion.For(error, schemas) is { } plan) {
                plans.Add(plan);
            }
        }

        return EmitterHarness.Write(ns => ProblemConversionEmitter.Emit(
            ns, schemas, plans, EmitterHarness.ModelsNamespace, "petstore"));
    }

    [Fact]
    public void TheMethodBuildsTheCaseFromTheRecord() {
        var emitted = Holder([Problem()], Error(404, "Problem"));

        Assert.Contains("public static class PetstoreProblems", emitted);
        Assert.Contains(
            $"public static {Shipped}NotFound<{Models}Problem> NotFoundProblem({Shipped}NotFound value) =>",
            emitted);
        Assert.Contains(
            $"new {Shipped}NotFound<{Models}Problem>(new {Models}Problem(value.Type, value.Title, value.Status, value.Detail))",
            emitted);
    }

    /// <summary>The record's Default converts to one shared case, so returning it allocates nothing.</summary>
    [Fact]
    public void TheDefaultInstanceConvertsToACachedCase() {
        var emitted = Holder([Problem()], Error(404, "Problem"));

        Assert.Contains($"ReferenceEquals(value, {Shipped}NotFound.Default) ? NotFoundProblemDefault :", emitted);
        Assert.Contains(
            $"private static readonly {Shipped}NotFound<{Models}Problem> NotFoundProblemDefault = " +
            $"new {Shipped}NotFound<{Models}Problem>(new {Models}Problem(" +
            $"{Shipped}NotFound.Default.Type, {Shipped}NotFound.Default.Title, " +
            $"{Shipped}NotFound.Default.Status, {Shipped}NotFound.Default.Detail));",
            emitted);
    }

    /// <summary>A record that carries a header hands it to the generic form beside the body.</summary>
    [Fact]
    public void AHeaderCarryingRecordPassesItsHeaderOn() {
        var emitted = Holder([Problem()], Error(429, "Problem"), Error(503, "Problem"));

        Assert.Contains($"new {Shipped}RateLimited<{Models}Problem>(value.RetryAfter, new {Models}Problem(", emitted);
        Assert.Contains($"new {Shipped}ServiceUnavailable<{Models}Problem>(new {Models}Problem(", emitted);
        Assert.Contains("), value.After)", emitted);
        // No Default for a record that needs a delay.
        Assert.DoesNotContain("RateLimitedProblemDefault", emitted);
    }

    /// <summary>MethodNotAllowed carries no problem members, so its body is the status's.</summary>
    [Fact]
    public void ARecordWithNoProblemMembersFillsTheBodyFromTheStatus() {
        var emitted = Holder([Problem()], Error(405, "Problem"));

        Assert.Contains(
            $"new {Models}Problem(\"about:blank\", \"Method Not Allowed\", value.Status, default), value.Allow)",
            emitted);
    }

    [Fact]
    public void ASmithyErrorShapeIsFilledFromTheDetailOrTheTitle() {
        var emitted = Holder([ErrorShape("TodoNotFound")], Named(404, "TodoNotFound", "TodoNotFoundError"));

        Assert.Contains(
            $"public static {Models}TodoNotFoundError NotFoundTodoNotFound({Shipped}NotFound value) =>",
            emitted);
        Assert.Contains(
            $"new {Models}TodoNotFoundError(new {Models}TodoNotFound((value.Detail ?? value.Title)))",
            emitted);
    }

    /// <summary>One method per record and body, however many operations declare the pair.</summary>
    [Fact]
    public void TwoOperationsSharingAnErrorShareTheMethod() {
        var emitted = Holder([Problem()], Error(404, "Problem"), Error(404, "Problem"));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(emitted, "NotFoundProblem\\("));
    }

    [Fact]
    public void NothingIsWrittenWhenNothingConverts() {
        var emitted = Holder([Plain("ApiError")], Error(404, "ApiError"));

        Assert.DoesNotContain("PetstoreProblems", emitted);
    }
}
