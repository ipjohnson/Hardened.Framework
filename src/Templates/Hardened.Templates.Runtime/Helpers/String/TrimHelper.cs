namespace Hardened.Templates.Runtime.Helpers.String;

public class TrimHelper : BaseStringHelper
{
    protected override object AugmentString(string stringValue)
    {
        return stringValue.Trim();
    }
}