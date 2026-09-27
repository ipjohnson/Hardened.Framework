using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// The bodies an operation declares for its failures, as the dictionary its handler info takes.
/// </summary>
public class DeclaredErrorBodiesTests
{
    private static ITypeDefinition Type(string name) => TypeDefinition.Get("TestApp", name);

    private static RequestHandlerModel Handler(
        IReadOnlyList<ResponseSchemaModel> responses,
        string? described = null
    ) =>
        new(
            new RequestHandlerNameModel("/photos", "POST"),
            Type("PhotoController"),
            "Upload",
            TypeDefinition.Get("TestApp.Generated", "PhotoController_Upload"),
            [],
            new ResponseInformationModel
            {
                ReturnType = Type("Photo"),
                DeclaredErrorBodiesExpression = described,
            },
            []
        )
        {
            ResponseSchemas = responses,
        };

    private static ResponseSchemaModel Failure(int status, string? instance) =>
        new(status, "A failure", null) { DeclaredInstance = instance };

    [Fact]
    public void EachStatusWithAnInstanceIsCarriedInStatusOrder()
    {
        Assert.Equal(
            "new global::System.Collections.Generic.Dictionary<int, object> { "
                + "{ 404, global::X.NotFound.Default }, { 413, global::X.ContentTooLarge.Default } }",
            HandlerInfoCodeGenerator.DeclaredErrorBodies(
                Handler([
                    Failure(413, "global::X.ContentTooLarge.Default"),
                    Failure(404, "global::X.NotFound.Default"),
                    Failure(409, null),
                ])
            )
        );
    }

    /// <summary>
    /// Two bodies at one status, as a <c>oneOf</c> declares: the first with an instance is the one
    /// a refusal at that status writes.
    /// </summary>
    [Fact]
    public void TheFirstInstanceAtAStatusIsTheOneCarried()
    {
        Assert.Equal(
            "new global::System.Collections.Generic.Dictionary<int, object> { "
                + "{ 409, global::X.Conflict.Default } }",
            HandlerInfoCodeGenerator.DeclaredErrorBodies(
                Handler([
                    Failure(409, "global::X.Conflict.Default"),
                    Failure(409, "global::X.Taken.Default"),
                ])
            )
        );
    }

    [Fact]
    public void AHandlerDeclaringNoInstanceCarriesNone()
    {
        Assert.Null(HandlerInfoCodeGenerator.DeclaredErrorBodies(Handler([Failure(404, null)])));
    }

    /// <summary>
    /// A contract's bodies are built by the bridge from the schemas it can fill, and passed
    /// through as they were built.
    /// </summary>
    [Fact]
    public void AContractsBodiesArePassedThrough()
    {
        Assert.Equal(
            "new Dictionary<int, object> { { 404, Bodies.NotFoundProblem } }",
            HandlerInfoCodeGenerator.DeclaredErrorBodies(
                Handler([], "new Dictionary<int, object> { { 404, Bodies.NotFoundProblem } }")
            )
        );
    }
}
