namespace Hardened.Requests.Abstract.Attributes;

public class HardenedFunctionAttribute : Attribute
{
    public HardenedFunctionAttribute(string? functionName = null)
    {
        FunctionName = functionName;
    }

    public string? FunctionName { get; }
}