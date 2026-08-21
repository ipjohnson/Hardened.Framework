using Hardened.SourceGenerator.Requests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// Recognising a return type as a declared response set, against a real compilation.
/// </summary>
/// <remarks>
/// <para>
/// The selector matches structurally - one public single-parameter constructor per case plus a
/// public <c>object? Value</c> - so what it needs is symbols, and symbols need a compilation. It
/// takes a <c>SemanticModel</c> rather than a <c>GeneratorSyntaxContext</c> precisely so that
/// compilation can be an ordinary one built here, instead of only ever existing inside a generator
/// run in another test project against another assembly's copy of this file - and this project
/// references the copy the specification-first generator ships.
/// </para>
/// <para>
/// The fixtures declare their own response set rather than referencing Hardened's, which keeps this
/// about the structural rule rather than about <c>Response&lt;T1..Tn&gt;</c> in particular - the
/// rule is what has to recognise a hand-rolled struct and a C# 15 union too.
/// </para>
/// </remarks>
public class UnionResponseSelectorSourceTests {

    private const string ResponseSet =
        """
        namespace Hardened.Requests.Abstract.Responses {
            public class HttpStatusAttribute : System.Attribute {
                public HttpStatusAttribute(int statusCode) { }
            }

            public interface IProvidesResponseHeaders { }
        }

        namespace App {
            using Hardened.Requests.Abstract.Responses;

            public sealed class Todo { }

            [HttpStatus(404)]
            public sealed class NotFound { }

            [HttpStatus(429)]
            public sealed class RateLimited : IProvidesResponseHeaders { }

            [HttpStatus(204)]
            public sealed class NoContent { }

            public readonly struct Result {
                public Result(Todo value) { Value = value; }
                public Result(NotFound value) { Value = value; }
                public Result(RateLimited value) { Value = value; }
                public Result(NoContent value) { Value = value; }
                public object? Value { get; }
            }

            public class Controller {
                public Result Handle() => default;
            }
        }
        """;

    /// <summary>
    /// The named handler's declaration, and the model to resolve it against.
    /// </summary>
    private static (SemanticModel Model, MethodDeclarationSyntax Method) Compile(
        string source, string methodName = "Handle") {
        var tree = CSharpSyntaxTree.ParseText(source);

        var compilation = CSharpCompilation.Create(
            "SelectorFixture",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);

        return (compilation.GetSemanticModel(tree), method);
    }

    private static IReadOnlyList<UnionCaseModel> Cases(string source, int? successStatus = null) {
        var (model, method) = Compile(source);

        return UnionResponseSelector.Decode(
            UnionResponseSelector.Read(model, method, successStatus));
    }

    #region recognising a set

    [Fact]
    public void AStructMatchingTheBasicUnionPatternIsAResponseSet() {
        var cases = Cases(ResponseSet);

        Assert.Equal(4, cases.Count);
        Assert.Equal(["App.Todo", "App.NotFound", "App.RateLimited", "App.NoContent"],
            cases.Select(c => c.TypeName.Replace("global::", "")));
    }

    /// <summary>
    /// Both halves are required. A public <c>object? Value</c> with no per-case constructor states
    /// no case set - that is an envelope, and an envelope has no type-to-status answer.
    /// </summary>
    [Fact]
    public void AnEnvelopeWithNoPerCaseConstructorIsNotAResponseSet() {
        Assert.Empty(Cases("""
            namespace App {
                public sealed class Todo { }
                public readonly struct Envelope {
                    public object? Value { get; }
                }
                public class Controller { public Envelope Handle() => default; }
            }
            """));
    }

    [Fact]
    public void ATypeWithNoValuePropertyIsNotAResponseSet() {
        Assert.Empty(Cases("""
            namespace App {
                public sealed class Todo { }
                public readonly struct Pair {
                    public Pair(Todo value) { Item = value; }
                    public object? Item { get; }
                }
                public class Controller { public Pair Handle() => default; }
            }
            """));
    }

    /// <summary>
    /// An ordinary return type is the path every handler in existence takes.
    /// </summary>
    [Fact]
    public void AnOrdinaryReturnTypeIsNotAResponseSet() {
        Assert.Empty(Cases("""
            namespace App {
                public sealed class Todo { }
                public class Controller { public Todo Handle() => null!; }
            }
            """));
    }

    /// <summary>
    /// A case appearing twice is rejected rather than deduplicated - the compiler reports CS0457 at
    /// the point of use, so a caller cannot construct one, and two arms of the same type would be
    /// unreachable code hiding a contradiction.
    /// </summary>
    [Fact]
    public void ASetWithARepeatedCaseIsRejected() {
        Assert.Empty(Cases("""
            namespace App {
                public sealed class Todo { }
                public readonly struct Result {
                    public Result(Todo value) { Value = value; }
                    public Result(Todo other) { Value = other; }
                    public object? Value { get; }
                }
                public class Controller { public Result Handle() => default; }
            }
            """));
    }

    #endregion

    #region unwrapping

    [Theory]
    [InlineData("System.Threading.Tasks.Task")]
    [InlineData("System.Threading.Tasks.ValueTask")]
    public void ASetIsFoundPastAnAwaitable(string awaitable) {
        var cases = Cases(ResponseSet.Replace(
            "public Result Handle() => default;",
            $"public {awaitable}<Result> Handle() => default!;"));

        Assert.Equal(4, cases.Count);
    }

    #endregion

    #region statuses, headers and bodies

    /// <summary>
    /// A case's own <c>[HttpStatus]</c> wins, and one without takes the endpoint's success status -
    /// which is what covers a POST that creates without annotating every case.
    /// </summary>
    [Fact]
    public void EachCaseTakesItsOwnStatusOrTheEndpointSuccessStatus() {
        var cases = Cases(ResponseSet, successStatus: 201);

        Assert.Equal(201, cases[0].Status);
        Assert.Equal(404, cases[1].Status);
        Assert.Equal(429, cases[2].Status);
        Assert.Equal(204, cases[3].Status);
    }

    [Fact]
    public void AnUnannotatedCaseDefaultsToTwoHundred() {
        Assert.Equal(200, Cases(ResponseSet)[0].Status);
    }

    /// <summary>
    /// Read off the interface, so the emitted switch calls ApplyHeaders only where there is
    /// something to apply rather than type-testing every response at run time.
    /// </summary>
    [Fact]
    public void OnlyACaseImplementingTheInterfaceContributesHeaders() {
        var cases = Cases(ResponseSet);

        Assert.False(cases[0].AppliesHeaders);
        Assert.False(cases[1].AppliesHeaders);
        Assert.True(cases[2].AppliesHeaders);
    }

    /// <summary>
    /// From the status, because 204 and 304 are the two RFC 9110 says have no body - a rule no
    /// response type can opt out of.
    /// </summary>
    [Fact]
    public void OnlyABodylessStatusHasNoBody() {
        var cases = Cases(ResponseSet);

        Assert.True(cases[0].HasBody);
        Assert.True(cases[1].HasBody);
        Assert.False(cases[3].HasBody);
    }

    #endregion

    #region findings

    private static string? Diagnose(string source, int? successStatus = null) {
        var (model, method) = Compile(source);

        return UnionResponseSelector.Diagnose(model, method, successStatus);
    }

    [Fact]
    public void AWellFormedSetHasNoFinding() {
        Assert.Null(Diagnose(ResponseSet));
    }

    [Fact]
    public void AnOrdinaryReturnTypeHasNoFinding() {
        Assert.Null(Diagnose("""
            namespace App {
                public sealed class Todo { }
                public class Controller { public Todo Handle() => null!; }
            }
            """));
    }

    /// <summary>
    /// Everything is assignable to <c>object</c>, so its arm would answer for every response the
    /// handler returns.
    /// </summary>
    [Fact]
    public void ObjectAsACaseIsFound() {
        var finding = Diagnose("""
            namespace App {
                public sealed class Todo { }
                public readonly struct Result {
                    public Result(Todo value) { Value = value; }
                    public Result(object value) { Value = value; }
                    public object? Value { get; }
                }
                public class Controller { public Result Handle() => default; }
            }
            """);

        Assert.NotNull(finding);
        Assert.StartsWith(UnionResponseSelector.UntypedFinding, finding, StringComparison.Ordinal);
    }

    /// <summary>
    /// The subtype's schema is a superset, so a payload validates against both statuses and
    /// <c>oneOf</c> requires exactly one match.
    /// </summary>
    [Fact]
    public void TwoAssignableCasesAtDifferentStatusesAreFound() {
        var finding = Diagnose("""
            namespace Hardened.Requests.Abstract.Responses {
                public class HttpStatusAttribute : System.Attribute {
                    public HttpStatusAttribute(int statusCode) { }
                }
            }

            namespace App {
                using Hardened.Requests.Abstract.Responses;

                public class Base { }

                [HttpStatus(404)]
                public class Derived : Base { }

                public readonly struct Result {
                    public Result(Base value) { Value = value; }
                    public Result(Derived value) { Value = value; }
                    public object? Value { get; }
                }
                public class Controller { public Result Handle() => default; }
            }
            """);

        Assert.NotNull(finding);
        Assert.StartsWith(UnionResponseSelector.AssignableFinding, finding, StringComparison.Ordinal);

        var fields = UnionResponseSelector.DecodeFinding(finding!);

        Assert.Equal(5, fields.Count);
        Assert.Contains("200", fields);
        Assert.Contains("404", fields);
    }

    /// <summary>
    /// Two cases of one status share a oneOf, where their relationship decides nothing - rejecting
    /// that would forbid the shape the design calls a oneOf within a 200.
    /// </summary>
    [Fact]
    public void TwoAssignableCasesAtOneStatusAreNotFound() {
        Assert.Null(Diagnose("""
            namespace App {
                public class Base { }
                public class Derived : Base { }
                public readonly struct Result {
                    public Result(Base value) { Value = value; }
                    public Result(Derived value) { Value = value; }
                    public object? Value { get; }
                }
                public class Controller { public Result Handle() => default; }
            }
            """));
    }

    /// <summary>
    /// Assignability through an interface counts too - a case implementing another case's interface
    /// is the same ambiguity as one deriving from it.
    /// </summary>
    [Fact]
    public void AssignabilityThroughAnInterfaceIsFound() {
        var finding = Diagnose("""
            namespace Hardened.Requests.Abstract.Responses {
                public class HttpStatusAttribute : System.Attribute {
                    public HttpStatusAttribute(int statusCode) { }
                }
            }

            namespace App {
                using Hardened.Requests.Abstract.Responses;

                public interface IProblem { }

                [HttpStatus(404)]
                public class NotFound : IProblem { }

                public readonly struct Result {
                    public Result(IProblem value) { Value = value; }
                    public Result(NotFound value) { Value = value; }
                    public object? Value { get; }
                }
                public class Controller { public Result Handle() => default; }
            }
            """);

        Assert.NotNull(finding);
        Assert.StartsWith(UnionResponseSelector.AssignableFinding, finding, StringComparison.Ordinal);
    }

    #endregion
}
