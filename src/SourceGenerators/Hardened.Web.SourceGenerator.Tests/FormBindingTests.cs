using System.Text.RegularExpressions;
using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Requests;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// What the generator does with <c>[FromForm]</c>.
/// </summary>
public class FormBindingTests
{
    private static readonly Type[] Anchors = [typeof(GetAttribute), typeof(FromBodyAttribute)];

    private static GeneratorResult Generate(string handlers, string types = "") =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string>
            {
                ["Test.cs"] = $$"""
                using System.Collections.Generic;
                using System.Text.Json.Serialization;
                using Hardened.Shared.Runtime.Attributes;
                using Hardened.Web.Runtime.Attributes;

                namespace TestApp;

                [HardenedModule]
                public partial class TestApplication { }

                public class Credentials {
                    public string Username { get; set; } = "";
                }

                {{types}}

                public class SignInController {
                {{handlers}}
                }
                """,
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors
        );

    /// <summary>The generated source with its whitespace removed, so a check is about the code.</summary>
    private static string Compact(string source) => Regex.Replace(source, @"\s+", "");

    private const string Search = """
        public record Search(int Page, int Size, string Status, string Q, int MinPrice = 0);
        """;

    /// <summary>
    /// The form is read once, whatever the number of fields bound from it.
    /// </summary>
    /// <remarks>
    /// Reading it reads the body, so a second read on a non-seekable stream returns nothing. Doing
    /// it once per handler makes that structural rather than something <c>FormReader</c> has to
    /// cache against a request it is a singleton relative to.
    /// </remarks>
    [Fact]
    public void TheFormIsReadOncePerHandler()
    {
        var result = Generate(
            """
                [Post("/sign-in")]
                public string SignIn(
                    [FromForm] string username, [FromForm] string password, [FromForm] string totp)
                    => username;
            """
        );

        result.AssertNoErrors();

        var source = result.SourceContaining("SignIn");
        var reads = source.Split("FormBinding.Read(").Length - 1;

        Assert.Equal(1, reads);
        Assert.Contains("form.Get(\"username\")", source);
        Assert.Contains("form.Get(\"password\")", source);
        Assert.Contains("form.Get(\"totp\")", source);
    }

    /// <summary>A handler with no form parameter never reads one.</summary>
    [Fact]
    public void AHandlerWithNoFormParameterDoesNotReadOne()
    {
        var result = Generate(
            """
                [Get("/whoami")]
                public string WhoAmI([FromQueryString] string id) => id;
            """
        );

        result.AssertNoErrors();

        Assert.DoesNotContain("FormBinding.Read(", result.SourceContaining("WhoAmI"));
    }

    /// <summary>The wire name comes from the attribute when it carries one.</summary>
    [Fact]
    public void AnAttributeNameOverridesTheParameterName()
    {
        var result = Generate(
            """
                [Post("/sign-in")]
                public string SignIn([FromForm("user_name")] string userName) => userName;
            """
        );

        result.AssertNoErrors();

        Assert.Contains("form.Get(\"user_name\")", result.SourceContaining("SignIn"));
    }

    /// <summary>
    /// Binding a form and a body on one handler is a build error.
    /// </summary>
    /// <remarks>
    /// There is one body and the two read it differently, so whichever runs second sees a consumed
    /// stream. The failure is otherwise a silently empty model or a silently empty set of fields on
    /// a handler that compiles and routes correctly.
    /// </remarks>
    [Fact]
    public void AFormAndABodyTogetherIsABuildError()
    {
        var result = Generate(
            """
                [Post("/sign-in")]
                public string SignIn([FromForm] string username, Credentials credentials)
                    => username;
            """
        );

        var reported = Assert.Single(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == FormAndBodyDiagnostics.DiagnosticId
        );

        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);
        Assert.Contains("username", reported.GetMessage());
        Assert.Contains("credentials", reported.GetMessage());
    }

    /// <summary>And a handler with only one of the two is not reported.</summary>
    [Theory]
    [InlineData("[FromForm] string username")]
    [InlineData("Credentials credentials")]
    public void OneOrTheOtherIsFine(string parameter)
    {
        var result = Generate(
            $$"""
                [Post("/sign-in")]
                public string SignIn({{parameter}}) => "";
            """
        );

        Assert.DoesNotContain(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == FormAndBodyDiagnostics.DiagnosticId
        );
    }

    /// <summary>
    /// A model is built from one field per member, and the form is still read once.
    /// </summary>
    [Fact]
    public void AModelIsBoundOneFieldPerMember()
    {
        var result = Generate(
            """
                [Post("/search")]
                public string Find([FromForm] Search search) => search.Q;
            """,
            Search
        );

        result.AssertNoErrors();

        var source = Compact(result.SourceContaining("Find"));

        Assert.Equal(1, source.Split("FormBinding.Read(").Length - 1);
        Assert.Contains("varsearchModel=newglobal::TestApp.Search(", source);
        Assert.Contains("ParseRequired<int>(form.Get(\"page\")!,\"page\")", source);
        Assert.Contains("ParseRequired<string>(form.Get(\"q\")!,\"q\")", source);
        Assert.Contains("parameters.search=searchModel;", source);
        Assert.DoesNotContain("form.Get(\"search\")", source);
    }

    /// <summary>
    /// A constructor default makes the member optional, the way it does for a parameter.
    /// </summary>
    [Fact]
    public void AConstructorDefaultIsTheFieldsDefault()
    {
        var result = Generate(
            """
                [Post("/search")]
                public string Find([FromForm] Search search) => search.Q;
            """,
            Search
        );

        result.AssertNoErrors();

        Assert.Contains(
            "ParseWithDefault<int>(form.Get(\"minPrice\")!,\"minPrice\",0)",
            Compact(result.SourceContaining("Find"))
        );
    }

    /// <summary>A field is named the way the serializer names the member.</summary>
    [Fact]
    public void AMemberIsNamedTheWayTheSerializerNamesIt()
    {
        var result = Generate(
            """
                [Post("/page")]
                public int Page([FromForm] Paging paging) => paging.Size;
            """,
            """
            public class Paging {
                [JsonPropertyName("page_size")]
                public int Size { get; set; }
            }
            """
        );

        result.AssertNoErrors();

        var source = result.SourceContaining("Page");

        Assert.Contains("form.Get(\"page_size\")", source);
        Assert.DoesNotContain("form.Get(\"size\")", source);
    }

    /// <summary>
    /// A member with an initializer is assigned only when its field was sent, so an absent field
    /// leaves the initializer's value.
    /// </summary>
    [Theory]
    [InlineData("FromForm", "form.Get")]
    [InlineData("FromQueryString", "context.Request.QueryString.Get")]
    public void AMemberWithAnInitializerIsAssignedOnlyWhenSent(string attribute, string read)
    {
        var result = Generate(
            $$"""
                [Post("/page")]
                public int Page([{{attribute}}] Paging paging) => paging.Size;
            """,
            """
            public class Paging {
                public int Size { get; set; } = 20;
                public List<string> Tags { get; set; } = new();
            }
            """
        );

        result.AssertNoErrors();

        var source = Compact(result.SourceContaining("Page"));

        Assert.Contains(
            "if(!global::Microsoft.Extensions.Primitives.StringValues.IsNullOrEmpty("
                + read
                + "(\"size\")))",
            source
        );
        Assert.Contains("if(" + read + "(\"tags\").Count>0)", source);
    }

    /// <summary>
    /// An <c>init</c> or <c>required</c> member is set in the object initializer, the one place C#
    /// allows it.
    /// </summary>
    [Fact]
    public void AnInitOnlyMemberIsSetInTheInitializer()
    {
        var result = Generate(
            """
                [Post("/page")]
                public string Page([FromForm] Paging paging) => paging.Sort;
            """,
            """
            public class Paging {
                public required string Sort { get; init; }
                public int? Size { get; init; }
            }
            """
        );

        result.AssertNoErrors();

        var source = Compact(result.SourceContaining("Page"));

        Assert.Contains("newglobal::TestApp.Paging{Sort=", source);
        Assert.Contains(
            "Size=context.KnownServices.StringConverterService.ParseOptional<int?>",
            source
        );
    }

    /// <summary>A query string model is bound the same way, from the query string.</summary>
    [Fact]
    public void AQueryStringModelIsBoundTheSameWay()
    {
        var result = Generate(
            """
                [Get("/search")]
                public string Find([FromQueryString] Search search) => search.Q;
            """,
            Search
        );

        result.AssertNoErrors();

        var source = Compact(result.SourceContaining("Find"));

        Assert.Contains(
            "ParseRequired<int>(context.Request.QueryString.Get(\"page\")!,\"page\")",
            source
        );
        Assert.DoesNotContain("FormBinding.Read(", source);
    }

    /// <summary>
    /// A type with its own <c>Parse</c> stays one field, because that is the type an
    /// <c>IStringConverter</c> is registered for.
    /// </summary>
    [Fact]
    public void ATypeWithItsOwnParseIsStillOneField()
    {
        var result = Generate(
            """
                [Post("/price")]
                public int Price([FromForm] Money price) => price.Cents;
            """,
            """
            public record Money(int Cents) {
                public static Money Parse(string text) => new(int.Parse(text));
            }
            """
        );

        result.AssertNoErrors();

        Assert.Contains(
            "ParseRequired<global::TestApp.Money>(form.Get(\"price\")!,\"price\")",
            Compact(result.SourceContaining("Price"))
        );
    }

    /// <summary>
    /// A collection the application declares binds as one value, as it did before models were
    /// bound member by member.
    /// </summary>
    [Fact]
    public void ACollectionTypeIsNotAModel()
    {
        var result = Generate(
            """
                [Get("/tags")]
                public int Tags([FromQueryString] TagList tags) => tags.Count;
            """,
            """
            public class TagList : List<string> { }
            """
        );

        result.AssertNoErrors();

        var source = Compact(result.SourceContaining("Tags"));

        Assert.Contains("ParseRequired<global::TestApp.TagList>", source);
        Assert.DoesNotContain("capacity", source);
    }

    /// <summary>A model the binder cannot build one field at a time is a build error.</summary>
    [Theory]
    [InlineData(
        "public record Model(string Name, Address Address); public record Address(string Street);",
        "[FromForm] Model model",
        "its member 'Address' is a 'TestApp.Address'"
    )]
    [InlineData("public record Model(string Name);", "[FromForm] Model? model", "it is nullable")]
    [InlineData(
        "public record Model(string Name);",
        "[FromForm(\"m\")] Model model",
        "the attribute cannot name a field"
    )]
    [InlineData(
        "public class Model { public int Size { get; init; } = 10; }",
        "[FromForm] Model model",
        "'Size' is init-only and has an initializer"
    )]
    [InlineData(
        "public class Model { public Model(int a) {} public Model(string b) {} }",
        "[FromQueryString] Model model",
        "more than one public constructor"
    )]
    [InlineData(
        "public class Model { private Model() {} }",
        "[FromForm] Model model",
        "has no public constructor"
    )]
    [InlineData(
        "public class Model { public int Size { get; } }",
        "[FromForm] Model model",
        "no constructor parameters or settable properties"
    )]
    public void AModelTheBinderCannotBuildIsABuildError(
        string types,
        string parameter,
        string expected
    )
    {
        var result = Generate(
            $$"""
                [Post("/model")]
                public string Bind({{parameter}}) => "";
            """,
            types
        );

        var reported = Assert.Single(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == BoundModelDiagnostics.DiagnosticId
        );

        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);
        Assert.Contains("'model'", reported.GetMessage());
        Assert.Contains(expected, reported.GetMessage());

        // The binder falls back to one field named after the parameter, so nothing but the
        // diagnostic fails the build.
        Assert.DoesNotContain(
            result.CompilationDiagnostics,
            d => d.Severity == DiagnosticSeverity.Error
        );
    }

    /// <summary>A member marked <c>[JsonIgnore]</c> cannot be set from a form either.</summary>
    [Fact]
    public void AnIgnoredMemberIsNotBound()
    {
        var result = Generate(
            """
                [Post("/account")]
                public string Update([FromForm] Account account) => account.Name;
            """,
            """
            public class Account {
                public string Name { get; set; } = "";
                [JsonIgnore]
                public bool IsAdmin { get; set; }
            }
            """
        );

        result.AssertNoErrors();

        Assert.DoesNotContain("isAdmin", result.SourceContaining("Update"));
    }

    /// <summary>
    /// Every member type the string converter reads from one value binds as a field, and the
    /// generated code compiles.
    /// </summary>
    [Fact]
    public void EveryScalarMemberTypeBindsAsOneField()
    {
        var result = Generate(
            """
                [Post("/everything")]
                public string Bind([FromForm] Everything everything) => "";
            """,
            """
            public enum Shade { Light, Dark }

            public class Everything {
                public string? Text { get; set; }
                public char Letter { get; set; }
                public bool Flag { get; set; }
                public byte Small { get; set; }
                public sbyte Signed { get; set; }
                public short Short { get; set; }
                public ushort UnsignedShort { get; set; }
                public uint Unsigned { get; set; }
                public long Long { get; set; }
                public ulong UnsignedLong { get; set; }
                public float Single { get; set; }
                public double Double { get; set; }
                public decimal Money { get; set; }
                public System.DateTime When { get; set; }
                public System.DateTimeOffset At { get; set; }
                public System.DateOnly Day { get; set; }
                public System.TimeOnly Time { get; set; }
                public System.TimeSpan Span { get; set; }
                public System.Guid Id { get; set; }
                public System.Uri? Link { get; set; }
                public Shade Shade { get; set; }
                public int? Maybe { get; set; }
                public byte[]? Bytes { get; set; }
                public string[]? Names { get; set; }
                public IReadOnlyList<System.Guid>? Ids { get; set; }
                public IEnumerable<Shade>? Shades { get; set; }
            }
            """
        );

        result.AssertNoErrors();

        var source = Compact(result.SourceContaining("Bind"));

        Assert.Contains("ParseRequired<global::System.Guid>(form.Get(\"id\")!,\"id\")", source);
        Assert.Contains("ParseRequired<global::TestApp.Shade>(form.Get(\"shade\")!", source);
        Assert.Contains("ParseOptionalMany<global::System.Guid>(form.Get(\"ids\")!", source);
    }

    /// <summary>
    /// A constructor default of each kind is written as C# the binder can pass on, whatever scope
    /// the model declared it in.
    /// </summary>
    [Fact]
    public void ConstructorDefaultsAreWrittenFromTheirConstants()
    {
        var result = Generate(
            """
                [Get("/defaults")]
                public string Bind([FromQueryString] Defaults defaults) => "";
            """,
            """
            public enum Shade { Light, Dark }

            public static class Limits {
                public const int Page = 3;
            }

            public record Defaults(
                long Long = 5,
                uint Unsigned = 7,
                ulong UnsignedLong = 9,
                float Single = 1.5f,
                double Double = 2.5,
                decimal Money = 3.5m,
                char Letter = 'x',
                string Text = "a \"quoted\" value",
                bool Flag = true,
                Shade Shade = Shade.Dark,
                Shade? Maybe = null,
                string? Missing = null,
                int Page = Limits.Page,
                System.Guid Id = default);
            """
        );

        result.AssertNoErrors();

        var source = Compact(result.SourceContaining("Bind"));

        Assert.Contains("\"long\",5L)", source);
        Assert.Contains("\"unsigned\",7U)", source);
        Assert.Contains("\"unsignedLong\",9UL)", source);
        Assert.Contains("\"flag\",true)", source);
        Assert.Contains("\"shade\",(global::TestApp.Shade)(1))", source);
        Assert.Contains("\"maybe\",null)", source);
        Assert.Contains("\"page\",3)", source);
        Assert.Contains("\"id\",default)", source);
        Assert.DoesNotContain("Limits.Page", source);
    }

    /// <summary>
    /// The constructor marked <c>[JsonConstructor]</c> is the one called, as the serializer would.
    /// </summary>
    [Fact]
    public void AJsonConstructorIsTheOneCalled()
    {
        var result = Generate(
            """
                [Post("/range")]
                public int Bind([FromForm] Span span) => span.Length;
            """,
            """
            public class Span {
                public Span(int start, int end) { Length = end - start; }

                [JsonConstructor]
                public Span(int length) { Length = length; }

                public int Length { get; }
            }
            """
        );

        result.AssertNoErrors();

        var source = Compact(result.SourceContaining("Bind"));

        Assert.Contains("form.Get(\"length\")", source);
        Assert.DoesNotContain("form.Get(\"start\")", source);
    }

    /// <summary>
    /// A property the model inherits binds like its own, and constructor arguments and an
    /// initializer can be used together.
    /// </summary>
    [Fact]
    public void InheritedMembersAndInitializersBindBesideConstructorArguments()
    {
        var result = Generate(
            """
                [Post("/order")]
                public string Bind([FromForm] Order order) => order.Sku;
            """,
            """
            public class Audited {
                public string? Note { get; set; }
            }

            public class Order : Audited {
                public Order(string sku) { Sku = sku; }

                public string Sku { get; }
                public required int Quantity { get; init; }
            }
            """
        );

        result.AssertNoErrors();

        var source = Compact(result.SourceContaining("Bind"));

        Assert.Contains("newglobal::TestApp.Order(context.KnownServices", source);
        Assert.Contains("){Quantity=", source);
        Assert.Contains("orderModel.Note=", source);
    }

    /// <summary>
    /// <c>[JsonIgnore]</c> with a <c>WhenWriting</c> condition is about the response, so the member
    /// still binds.
    /// </summary>
    [Fact]
    public void AMemberIgnoredOnlyWhenWritingStillBinds()
    {
        var result = Generate(
            """
                [Post("/account")]
                public string Update([FromForm] Account account) => account.Name ?? "";
            """,
            """
            public class Account {
                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                public string? Name { get; set; }
            }
            """
        );

        result.AssertNoErrors();

        Assert.Contains("form.Get(\"name\")", result.SourceContaining("Update"));
    }
}
