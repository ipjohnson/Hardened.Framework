namespace Hardened.Shared.Testing.Attributes;

/// <summary>
/// Apply to test to set environment value for a test
/// </summary>
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