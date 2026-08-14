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
/// <b>Both halves are written by hand rather than run.</b> The validation generator lives in an
/// assembly this project cannot reference - it carries its own copy of these sources, and
/// referencing both makes every shared type ambiguous. So the marker it declares and the validator
/// it would emit are supplied as ordinary source. That makes this the sharpest available test of
/// the convention: the generated code names <c>OrderValidator</c> without ever seeing it,
/// and if the name it derives were wrong, the case would not compile.
/// </para>
/// </remarks>
public class FunctionValidationTests {

    /// <summary>
    /// Stands in for what <c>Hardened.Validation.SourceGenerator</c> contributes: the marker that
    /// says it is running, and a validator named the way it names them.
    /// </summary>
    private const string ValidationGeneratorOutput = """
        using ValidationModules;

        namespace Hardened.Validation.Generated {
            internal static class ValidationGeneratorMarker { }
        }

        namespace TestApp {
            public sealed class OrderValidator : IValidatorFor<Order> {
                public OrderValidator() { }

                public void Validate(ref ValidationContext ctx, Order value) { }
            }
        }

        """;

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
    public void AConstrainedPayloadAttachesAFilter() {
        var handler = FunctionGeneratorHarness.Generate(
                new Dictionary<string, string> {
                    ["Functions.cs"] = FunctionSource,
                    ["Validation.cs"] = ValidationGeneratorOutput
                })
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        Assert.Contains("ValidationFilterProvider<Parameters>", handler);
    }

    /// <summary>
    /// The validator emitted for the payload is the one that evaluates the constraints; the
    /// parameters validator only reaches it.
    /// </summary>
    [Fact]
    public void TheParametersValidatorDelegatesToThePayloadValidator() {
        var validator = FunctionGeneratorHarness.Generate(
                new Dictionary<string, string> {
                    ["Functions.cs"] = FunctionSource,
                    ["Validation.cs"] = ValidationGeneratorOutput
                })
            .AssertNoErrors()
            .SourceContaining("ParametersValidator");

        Assert.Contains("new global::TestApp.OrderValidator()", validator);
    }

    /// <summary>
    /// Without the marker nothing is attached, which is what keeps a project that never referenced
    /// the validation generator building.
    /// </summary>
    [Fact]
    public void WithoutTheValidationGeneratorNothingIsAttached() {
        var handler = FunctionGeneratorHarness.Generate(FunctionSource)
            .AssertNoErrors()
            .SourceContaining("Process.FunctionHandler");

        Assert.DoesNotContain("ValidationFilterProvider", handler);
    }
}
