using System;
using System.Collections.Generic;
using System.Text;

namespace Hardened.SourceGenerator.Web.Routing
{
    public class RouteTreeNode<T>
    {
        public RouteTreeNode(
            string path,
            IReadOnlyList<RouteTreeNode<T>> childNodes, 
            IReadOnlyList<RouteTreeNode<T>> wildCardNodes,
            IReadOnlyList<RouteTreeLeafNode<T>> leafNodes)
        {
            Path = path;
            ChildNodes = childNodes;
            WildCardNodes = wildCardNodes;
            LeafNodes = leafNodes;
        }

        public string Path { get; }

        public IReadOnlyList<RouteTreeNode<T>> ChildNodes { get; }

        public IReadOnlyList<RouteTreeNode<T>> WildCardNodes { get; }

        public IReadOnlyList<RouteTreeLeafNode<T>> LeafNodes { get; }
    }
}
