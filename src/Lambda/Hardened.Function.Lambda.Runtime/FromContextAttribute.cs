namespace Hardened.Function.Lambda.Runtime
{
    public class FromContextAttribute : Attribute
    {
        public FromContextAttribute(string? name = null)
        {
            Name = name;
        }

        public string? Name { get; }
    }
}
