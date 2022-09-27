namespace Hardened.SourceGenerator.Web.Routing
{
    public class RouteTreeLeafNode<T>
    {
        public RouteTreeLeafNode(string method, T value)
        {
            Method = method;
            Value = value;
        }

        public string Method { get; }

        public T Value { get; }
    }
}
