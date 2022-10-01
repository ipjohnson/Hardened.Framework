namespace Hardened.Shared.Testing.Attributes;

/// <summary>
/// Used to specify an entry point for a test
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public class HardenedTestEntryPointAttribute : Attribute
{
    public HardenedTestEntryPointAttribute(Type entryPoint)
    {
        EntryPoint = entryPoint;
    }

    public Type EntryPoint { get; }
}