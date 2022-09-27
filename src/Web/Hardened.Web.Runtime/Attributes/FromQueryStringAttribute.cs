namespace Hardened.Web.Runtime.Attributes
{
    public class FromQueryStringAttribute : Attribute
    {
        public FromQueryStringAttribute(string? name = null)
        {
            Name = name;
        }

        public string? Name { get; }
    }
}
