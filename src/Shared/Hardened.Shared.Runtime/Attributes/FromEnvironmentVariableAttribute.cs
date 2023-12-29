namespace Hardened.Shared.Runtime.Attributes;

/// <summary>
/// Attribute ConfigurationModel properties to populate from environment
/// </summary>
public class FromEnvironmentVariableAttribute : Attribute {
    public FromEnvironmentVariableAttribute(string environmentVariable) {
        EnvironmentVariable = environmentVariable;
    }

    public string EnvironmentVariable { get; }
}