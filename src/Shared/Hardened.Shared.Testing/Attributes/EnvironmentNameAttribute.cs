namespace Hardened.Shared.Testing.Attributes;

public class EnvironmentNameAttribute : Attribute
{
    public EnvironmentNameAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }
}