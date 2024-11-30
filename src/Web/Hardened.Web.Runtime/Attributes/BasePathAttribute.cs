namespace Hardened.Web.Runtime.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public class BasePathAttribute : Attribute {
    public string Path {
        get;
    }

    public BasePathAttribute(string path) {
        Path = path;
    }
}