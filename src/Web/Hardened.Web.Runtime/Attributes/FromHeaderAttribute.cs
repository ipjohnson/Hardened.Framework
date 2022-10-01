namespace Hardened.Web.Runtime.Attributes;

public class FromHeaderAttribute : Attribute
{
    public FromHeaderAttribute(string? name = null)
    {
        Name = name;
    }

    public string? Name { get;  }
}