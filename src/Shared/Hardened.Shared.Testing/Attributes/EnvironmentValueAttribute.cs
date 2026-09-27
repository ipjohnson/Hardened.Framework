namespace Hardened.Shared.Testing.Attributes;

/// <summary>
/// A variable in the test's environment, which <c>IHardenedEnvironment.Value&lt;T&gt;</c> reads.
/// </summary>
/// <remarks>
/// On a method, a class or the assembly, as many as the test needs on each. The runner reads them
/// all, so a test that needed two variables had to write an <c>IHardenedTestEnvironmentAttribute</c>
/// while this allowed one.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly,
    AllowMultiple = true
)]
public class EnvironmentValueAttribute : Attribute
{
    public EnvironmentValueAttribute(string variable, string value)
    {
        Variable = variable;
        Value = value;
    }

    public string Variable { get; }

    public string Value { get; }
}
