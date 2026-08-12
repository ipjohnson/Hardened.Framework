using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Shared;

/// <summary>
/// What a <c>[HardenedModule]</c> class looks like by the time a generator sees it.
///
/// <para>
/// Every Hardened generator starts from this one model. The application root reads its methods to
/// decide whether to wire <c>Startup</c> and <c>ConfigureLogging</c>; the routing table reads its
/// type and namespace; the configuration generator reads its properties. A member the transform
/// drops is a feature that silently does not exist.
/// </para>
/// </summary>
public class EntryPointModelTests {

    [Fact]
    public void TheEntryPointCarriesTheDeclaringTypeAndNamespace() {
        var model = EntryPointCapture.Single(EntryPointCapture.Application());

        Assert.Equal("TestApp", model.EntryPointType.Namespace);
        Assert.Equal("Application", model.EntryPointType.Name);
    }

    /// <summary>
    /// The root flag is fixed by whichever provider the generator wired, not read from source — a
    /// module generator passes false and an application generator passes true over the same syntax.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheRootFlagIsWhicheverTheProviderWasBuiltWith(bool rootEntryPoint) {
        var model = EntryPointCapture.Single(EntryPointCapture.Application(), rootEntryPoint);

        Assert.Equal(rootEntryPoint, model.RootEntryPoint);
    }

    /// <summary>
    /// Methods carry name, return type and parameters. <c>ApplicationEntryPointFileWriter</c>
    /// selects on the name and the parameter count, so a method reaching the model without its
    /// parameters is wired the wrong way round.
    /// </summary>
    [Fact]
    public void MethodsReachTheModelWithTheirReturnTypeAndParameters() {
        var model = EntryPointCapture.Single(EntryPointCapture.Application("""
                public string Describe(int count, string name) => name;
            """));

        var method = Assert.Single(model.MethodDefinitions, definition => definition.Name == "Describe");

        Assert.Equal("System.String", method.ReturnType?.ToString());
        Assert.Equal(["count", "name"], method.Parameters.Select(parameter => parameter.Name));
        Assert.Equal("System.String Describe(System.Int32 count,System.String name)", method.ToString());
    }

    [Fact]
    public void AMethodWithNoParametersReachesTheModelWithAnEmptyParameterList() {
        var model = EntryPointCapture.Single(EntryPointCapture.Application("""
                public void Configure() { }
            """));

        var method = Assert.Single(model.MethodDefinitions, definition => definition.Name == "Configure");

        Assert.Empty(method.Parameters);
        Assert.Equal("System.Void Configure()", method.ToString());
    }

    /// <summary>
    /// Two methods of the same name and different signatures are two model entries. The writer
    /// looks up <c>ConfigureLogging</c> by name and then branches on parameter count, so collapsing
    /// overloads would make which one it finds depend on declaration order.
    /// </summary>
    [Fact]
    public void OverloadsReachTheModelSeparately() {
        var model = EntryPointCapture.Single(EntryPointCapture.Application("""
                public void Configure() { }
                public void Configure(int value) { }
            """));

        Assert.Equal(2, model.MethodDefinitions.Count(definition => definition.Name == "Configure"));
    }

    /// <summary>
    /// Only settable public instance properties. The configuration generator writes to every
    /// property the model lists, so a get-only or static one arriving here becomes an assignment
    /// that does not compile.
    /// </summary>
    [Fact]
    public void OnlySettablePublicInstancePropertiesReachTheModel() {
        var model = EntryPointCapture.Single(EntryPointCapture.Application("""
                public string Settable { get; set; } = "";
                public string Initable { get; init; } = "";
                public string GetOnly { get; } = "";
                public string Expression => "";
                public static string Static { get; set; } = "";
                private string Private { get; set; } = "";
                internal string Internal { get; set; } = "";
            """));

        Assert.Equal(
            ["Settable", "Initable"],
            model.PropertyDefinitions!.Select(property => property.PropertyName));
    }

    [Fact]
    public void APropertyReachesTheModelWithItsType() {
        var model = EntryPointCapture.Single(EntryPointCapture.Application("""
                public int Count { get; set; }
            """));

        var property = Assert.Single(model.PropertyDefinitions!);

        Assert.Equal("Count", property.PropertyName);
        Assert.Equal("System.Int32", property.PropertyType.ToString());
    }

    [Fact]
    public void AnEntryPointWithNoPropertiesReachesTheModelWithAnEmptyList() {
        var model = EntryPointCapture.Single(EntryPointCapture.Application());

        Assert.Empty(model.PropertyDefinitions!);
    }

    /// <summary>
    /// The attribute that selected the class is itself in the model, because a generator that
    /// selects on <c>[HardenedModule]</c> still has to read the other attributes beside it.
    /// </summary>
    [Fact]
    public void TheModuleAttributeReachesTheModel() {
        var model = EntryPointCapture.Single(EntryPointCapture.Application());

        var attribute = Assert.Single(model.AttributeModels);

        Assert.Equal("HardenedModuleAttribute", attribute.TypeDefinition.Name);
        Assert.Equal("Hardened.Shared.Runtime.Attributes", attribute.TypeDefinition.Namespace);
    }

    /// <summary>
    /// Positional arguments and named property assignments are kept apart, because they are emitted
    /// into different slots: the arguments go to the attribute's constructor and the assignments to
    /// an object initialiser after it.
    /// </summary>
    [Fact]
    public void PositionalArgumentsAndNamedAssignmentsAreKeptApart() {
        var model = EntryPointCapture.Single(EntryPointCapture.Application(
            attributes: "[Audit(\"orders\", 2, Scope = \"tenant\", Level = 3)]",
            trailing: AuditAttribute));

        var audit = Assert.Single(model.AttributeModels,
            attribute => attribute.TypeDefinition.Name == "AuditAttribute");

        Assert.Equal("\"orders\", 2", audit.Arguments);
        Assert.Equal("Scope = \"tenant\", Level = 3", audit.PropertyAssignment);
    }

    [Fact]
    public void AnAttributeWithNoArgumentsCarriesNeither() {
        var model = EntryPointCapture.Single(EntryPointCapture.Application(
            attributes: "[Audit]", trailing: AuditAttribute));

        var audit = Assert.Single(model.AttributeModels,
            attribute => attribute.TypeDefinition.Name == "AuditAttribute");

        Assert.Equal("", audit.Arguments);
        Assert.Equal("", audit.PropertyAssignment);
    }

    /// <summary>
    /// An attribute class is not required to be named <c>*Attribute</c>. The suffix is added back so
    /// the emitted code names the type rather than the shorthand — <c>[Trace]</c> declared as
    /// <c>class Trace : Attribute</c> has to be emitted as <c>TraceAttribute</c>, which is the only
    /// spelling that resolves from an unrelated namespace.
    /// </summary>
    [Fact]
    public void AnAttributeClassWithoutTheSuffixGetsItBack() {
        var model = EntryPointCapture.Single(EntryPointCapture.Application(
            attributes: "[Trace]",
            trailing: """
                public class Trace : Attribute { }
                """));

        Assert.Single(model.AttributeModels, attribute => attribute.TypeDefinition.Name == "TraceAttribute");
    }

    /// <summary>
    /// A name the compiler cannot resolve is <em>kept</em> in the model, carrying an empty
    /// namespace — it renders as <c>.NotDeclaredAnywhereAttribute</c>, with a leading dot where the
    /// namespace should be.
    ///
    /// <para>
    /// The reasonable expectation is that it is dropped: an unresolvable name has no type, so there
    /// is nothing meaningful to emit. Recorded 2026-08-12 as observed behaviour, not asserted as
    /// correct. It is latent rather than live — the input that produces it does not compile, so a
    /// real build fails before any generated file matters, and no emitter has been shown to write
    /// the malformed name out. Worth resolving deliberately: either drop the attribute at model
    /// build time, or confirm no emit path can reach it.
    /// </para>
    ///
    /// <para>
    /// The transform producing a model rather than throwing IS the load-bearing behaviour here, and
    /// that part works — it is what keeps the IDE responsive while a user is mid-edit.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUnresolvableAttributeIsKeptWithAnEmptyNamespace() {
        var generator = new EntryPointCaptureGenerator();

        var result = Hardened.SourceGeneration.Testing.GeneratorTestHarness.Run(
            EntryPointCapture.Application(attributes: "[NotDeclaredAnywhere]"),
            generator,
            RequestGeneratorHarness.Anchors);

        // The input itself does not compile, which is the point: the transform still has to produce
        // a model rather than throw, so the IDE keeps working while the user is mid-edit.
        var model = Assert.Single(generator.Models);

        var unresolvable = Assert.Single(model.AttributeModels,
            attribute => attribute.TypeDefinition.Name.StartsWith("NotDeclaredAnywhere", StringComparison.Ordinal));

        // The empty namespace is the part worth pinning: it is what would render as a leading dot.
        Assert.Empty(unresolvable.TypeDefinition.Namespace);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// The type shapes a parameter can take, and what each becomes in the model. Generic arguments,
    /// arrays and nullables each go down their own branch, and a branch that produces the wrong
    /// type definition emits a signature that does not compile.
    ///
    /// <para>
    /// Note where the C# keyword aliases survive and where they do not. A parameter declared as a
    /// bare <c>int</c> reaches the model as <c>System.Int32</c>, but the same keyword nested inside
    /// a generic argument, an array element or a <c>Nullable</c> stays as it was written —
    /// <c>List&lt;short&gt;</c>, <c>int[]</c>, <c>int?</c>. Named types are qualified at every
    /// depth, so <c>List&lt;DayOfWeek&gt;</c> does become
    /// <c>List&lt;System.DayOfWeek&gt;</c>. That asymmetry is why <c>string?</c> qualifies and
    /// <c>int?</c> does not: the first is a reference type carrying a nullable annotation, the
    /// second is <c>Nullable&lt;int&gt;</c> and goes down the generic-argument branch.
    /// </para>
    ///
    /// <para>
    /// Both forms compile, so nothing downstream breaks. Recorded 2026-08-12 because the shapes
    /// look inconsistent read cold, and a future normalisation should be a deliberate change rather
    /// than an accident.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("short", "System.Int16")]
    [InlineData("int", "System.Int32")]
    [InlineData("long", "System.Int64")]
    [InlineData("string", "System.String")]
    [InlineData("DayOfWeek", "System.DayOfWeek")]
    [InlineData("IDisposable", "System.IDisposable")]
    [InlineData("Outer.Inner", "TestApp.Outer.Inner")]
    [InlineData("int?", "int?")]
    [InlineData("string?", "System.String?")]
    [InlineData("int[]", "int[]")]
    [InlineData("List<short>", "System.Collections.Generic.List<short>")]
    [InlineData("List<ushort>", "System.Collections.Generic.List<ushort>")]
    [InlineData("List<uint>", "System.Collections.Generic.List<uint>")]
    [InlineData("List<ulong>", "System.Collections.Generic.List<ulong>")]
    [InlineData("List<long>", "System.Collections.Generic.List<long>")]
    [InlineData("List<string>", "System.Collections.Generic.List<string>")]
    [InlineData("List<int[]>", "System.Collections.Generic.List<int[]>")]
    [InlineData("List<DayOfWeek>", "System.Collections.Generic.List<System.DayOfWeek>")]
    [InlineData("List<Outer.Inner>", "System.Collections.Generic.List<TestApp.Outer.Inner>")]
    [InlineData("List<int>?", "System.Collections.Generic.List<int>?")]
    [InlineData("Dictionary<string, int>",
        "System.Collections.Generic.Dictionary<string,int>")]
    public void EveryParameterShapeReachesTheModelAsItsOwnTypeDefinition(
        string declaration, string expected) {
        var model = EntryPointCapture.Single(EntryPointCapture.Application(
            $"    public void Handle({declaration} value) {{ }}",
            trailing: NestedType));

        var method = Assert.Single(model.MethodDefinitions, definition => definition.Name == "Handle");

        Assert.Equal(expected, Render(Assert.Single(method.Parameters).Type));
    }

    /// <summary>
    /// A generic method's type parameter has no namespace of its own, so it is carried through by
    /// name — the emitted signature repeats the declaration's own <c>T</c>.
    /// </summary>
    [Fact]
    public void AGenericMethodsTypeParameterIsCarriedThroughByName() {
        var model = EntryPointCapture.Single(EntryPointCapture.Application("""
                public void Handle<T>(List<T> values) { }
            """));

        var method = Assert.Single(model.MethodDefinitions, definition => definition.Name == "Handle");

        Assert.Equal(
            "System.Collections.Generic.List<T>",
            Render(Assert.Single(method.Parameters).Type));
    }

    /// <summary>
    /// A method the compiler resolved to nothing still has to produce a model. <c>void</c> is the
    /// one return type with no symbol behind its keyword in the same way the others have.
    /// </summary>
    [Fact]
    public void AVoidReturnTypeReachesTheModel() {
        var model = EntryPointCapture.Single(EntryPointCapture.Application("""
                public void Handle() { }
            """));

        var method = Assert.Single(model.MethodDefinitions, definition => definition.Name == "Handle");

        Assert.Equal("System.Void", Render(method.ReturnType!));
    }

    [Fact]
    public void AGenericReturnTypeReachesTheModelClosed() {
        var model = EntryPointCapture.Single(EntryPointCapture.Application("""
                public Task<string> Handle() => Task.FromResult("");
            """));

        var method = Assert.Single(model.MethodDefinitions, definition => definition.Name == "Handle");

        // Task<string>, not Task<System.String> — the generic argument keeps the keyword it was
        // written with. See EveryParameterShapeReachesTheModelAsItsOwnTypeDefinition.
        Assert.Equal("System.Threading.Tasks.Task<string>", Render(method.ReturnType!));
    }

    private const string AuditAttribute = """
        public class AuditAttribute : Attribute {
            public AuditAttribute(string name = "", int version = 0) { }

            public string Scope { get; set; } = "";

            public int Level { get; set; }
        }
        """;

    private const string NestedType = """
        public class Outer {
            public class Inner { }
        }
        """;

    /// <summary>
    /// Namespace, name, generic arguments, array and nullable — everything the emitter writes out.
    /// <c>ITypeDefinition.ToString</c> drops the array and nullable markers on a non-generic type,
    /// so it cannot tell <c>int</c> from <c>int[]</c>.
    /// </summary>
    private static string Render(ITypeDefinition type) {
        var text = type.Namespace.Length > 0 ? type.Namespace + "." + type.Name : type.Name;

        if (type.TypeArguments.Count > 0) {
            text += "<" + string.Join(",", type.TypeArguments.Select(Render)) + ">";
        }

        if (type.IsArray) {
            text += "[]";
        }

        if (type.IsNullable) {
            text += "?";
        }

        return text;
    }
}
