using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Function;

/// <summary>
/// A <c>[HardenedFunction]</c> whose payload carries constraints gets the same treatment a
/// controller does.
/// </summary>
/// <remarks>
/// <para>
/// The web and function front-ends share the attachment code, so what is really under test here is
/// that the function pipeline is wired to it - the two generators diverged for long enough that
/// assuming they behave alike is how one of them silently stops validating.
/// </para>
/// <para>
/// <b>ValidationModules' generator is stood in for rather than run.</b> Its assembly carries its own
/// copy of the ValidationModules sources this project compiles against, and referencing both makes
/// every shared type ambiguous. So the build property its package makes visible is passed, and the
/// validator it would emit is supplied as ordinary source. That makes this the sharpest available
/// test of the convention: the generated code names <c>OrderValidator</c> without ever seeing it,
/// and if the name it derives were wrong, the case would not compile.
/// </para>
/// </remarks>
public class FunctionValidationTests
{
    /// <summary>
    /// Stands in for what ValidationModules' generator contributes: a validator named the way it
    /// names them.
    /// </summary>
    private const string ValidationGeneratorOutput = """
        using ValidationModules;

        namespace TestApp {
            public sealed class OrderValidator : IValidatorFor<Order> {
                public OrderValidator() { }

                public ValidationFlow Validate(ref ValidationContext ctx, Order value) =>
                    ValidationFlow.Continue;
            }
        }

        """;

    /// <summary>What the ValidationModules.SourceGenerator package makes visible to the compiler.</summary>
    private static readonly Dictionary<string, string> ValidationModulesPackage = new()
    {
        ["ValidationModules_Registration"] = "",
    };

    private const string FunctionSource = """
        using System.Threading.Tasks;
        using ValidationModules.Constraints;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Shared.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class TestApplication { }

        public class Order {
            [Required]
            public string? Reference { get; set; }
        }

        public class TestFunctions {
            [HardenedFunction]
            public string Process(Order order) => order.Reference ?? "";
        }
        """;

    [Fact]
    public void AConstrainedPayloadAttachesAFilter()
    {
        var handler = FunctionGeneratorHarness
            .Generate(
                new Dictionary<string, string>
                {
                    ["Functions.cs"] = FunctionSource,
                    ["Validation.cs"] = ValidationGeneratorOutput,
                },
                ValidationModulesPackage
            )
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        // Fully qualified from CSharpAuthor 2.0 on: the nested Parameters class is named by its
        // full name, because a bare name would be qualified to global:: and resolve nothing.
        Assert.Contains("ValidationFilterProvider<global::", handler);
        Assert.Contains(".Parameters>", handler);
    }

    /// <summary>
    /// The validator emitted for the payload is the one that evaluates the constraints; the
    /// parameters validator only reaches it.
    /// </summary>
    [Fact]
    public void TheParametersValidatorDelegatesToThePayloadValidator()
    {
        var validator = FunctionGeneratorHarness
            .Generate(
                new Dictionary<string, string>
                {
                    ["Functions.cs"] = FunctionSource,
                    ["Validation.cs"] = ValidationGeneratorOutput,
                },
                ValidationModulesPackage
            )
            .AssertNoErrors()
            .SourceContaining("ParametersValidator");

        Assert.Contains("new global::TestApp.OrderValidator()", validator);
    }

    /// <summary>
    /// Without the build property nothing is attached, which is what keeps a project that never
    /// referenced ValidationModules' generator building.
    /// </summary>
    [Fact]
    public void WithoutTheValidationGeneratorNothingIsAttached()
    {
        var handler = FunctionGeneratorHarness
            .Generate(FunctionSource)
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        Assert.DoesNotContain("ValidationFilterProvider", handler);
    }
}
