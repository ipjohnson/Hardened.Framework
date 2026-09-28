using Hardened.Aws.Lambda.Runtime.Development;
using Xunit;

namespace Hardened.Aws.Lambda.Runtime.Tests.Development;

/// <summary>
/// What the emulator sets on the function's process: the variables the Lambda service would have
/// set for a function deployed the way the documentation says.
/// </summary>
public class LambdaEmulatorTests
{
    private static readonly LambdaEmulatorPlan Plan = LambdaEmulatorPlan.From(
        "Orders",
        apiGateway: false,
        _ => null
    )!;

    private static Dictionary<string, string?> Point(Dictionary<string, string?> variables)
    {
        LambdaEmulator.Point(
            Plan,
            name => variables.GetValueOrDefault(name),
            (name, value) => variables[name] = value
        );

        return variables;
    }

    [Fact]
    public void ALocalRunLogsJson()
    {
        var variables = Point(new Dictionary<string, string?>());

        Assert.Equal(Plan.RuntimeApiEndpoint, variables[LambdaEmulator.RuntimeApiVariable]);
        Assert.Equal("JSON", variables[LambdaEmulator.LogFormatVariable]);
    }

    [Fact]
    public void ALogFormatAlreadySetIsKept()
    {
        var variables = Point(
            new Dictionary<string, string?> { [LambdaEmulator.LogFormatVariable] = "Text" }
        );

        Assert.Equal("Text", variables[LambdaEmulator.LogFormatVariable]);
    }
}
