namespace Hardened.SourceGenerator.Web.Routing
{
    public class RouteTreeNode<T>
    {
        public RouteTreeNode(
            string path,
            IReadOnlyList<RouteTreeNode<T>> childNodes, 
            IReadOnlyList<RouteTreeNode<T>> wildCardNodes,
            IReadOnlyList<RouteTreeLeafNode<T>> leafNodes, 
            int wildCardDepth)
        {
            Path = path == "\0" ? "" : path;
            ChildNodes = childNodes;
            WildCardNodes = wildCardNodes;
            LeafNodes = leafNodes;
            WildCardDepth = wildCardDepth;
        }

        public int WildCardDepth { get; }

        public string Path { get; }

        public IReadOnlyList<RouteTreeNode<T>> ChildNodes { get; }

        public IReadOnlyList<RouteTreeNode<T>> WildCardNodes { get; }

        public IReadOnlyList<RouteTreeLeafNode<T>> LeafNodes { get; }
    }
}
