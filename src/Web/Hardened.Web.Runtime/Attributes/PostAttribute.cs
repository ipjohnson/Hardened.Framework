namespace Hardened.Web.Runtime.Attributes;

public class PostAttribute : Attribute
{
    public PostAttribute(string path = "")
    {
        Path = path;
    }

    public string Path { get; }
}