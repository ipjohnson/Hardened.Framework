namespace Hardened.Templates.Runtime.Helpers.String;

public class ToUpperHelper : BaseStringHelper
{
    protected override object AugmentString(string stringValue)
    {
        return stringValue.ToUpperInvariant();
    }
}