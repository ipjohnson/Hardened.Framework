using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.SourceGenerator.Web.Routing;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web.Routing
{
    public class SimpleWildCardRoutingTests
    {
        [Fact]
        public void SingleWildCardRoute()
        {
            var routes = new List<RouteTreeGenerator<string>.Entry>
            {
                new ("/api/person/{id}", "GET", "Person"),
            };

            var generator = new RouteTreeGenerator<string>();

            var routeTree = generator.GenerateTree(routes);
            
            routeTree.AssertPath("/");
            routeTree.ChildNodes.AssertCount(1);
            routeTree.AssertNoLeafNodes();
            routeTree.AssertNoWildCardNodes();

            var childNode = routeTree.ChildNodes[0];

            childNode.AssertPath("api/person/");
            childNode.AssertNoLeafNodes();
            childNode.WildCardNodes.AssertCount(1);
            childNode.AssertNoChildren();

            var wildCardNode = childNode.WildCardNodes[0];
            wildCardNode.AssertNoChildren();
            wildCardNode.AssertNoWildCardNodes();
            wildCardNode.LeafNodes.AssertCount(1);
            Assert.Equal(1, wildCardNode.WildCardDepth);

            var leafNode = wildCardNode.LeafNodes[0];
            
            Assert.Equal("GET", leafNode.Method);
            Assert.Equal("Person", leafNode.Value);
        }


        [Fact]
        public void DoubleWildCardRoute()
        {
            var routes = new List<RouteTreeGenerator<string>.Entry>
            {
                new ("/api/company/{company}/person/{id}", "GET", "Person"),
            };

            var generator = new RouteTreeGenerator<string>();

            var routeTree = generator.GenerateTree(routes);

            routeTree.AssertPath("/");
            routeTree.ChildNodes.AssertCount(1);
            routeTree.AssertNoLeafNodes();
            routeTree.AssertNoWildCardNodes();

            var childNode = routeTree.ChildNodes[0];

            childNode.AssertPath("api/company/");
            childNode.AssertNoLeafNodes();
            childNode.WildCardNodes.AssertCount(1);
            childNode.AssertNoChildren();

            var wildCardNode = childNode.WildCardNodes[0];
            wildCardNode.ChildNodes.AssertCount(1);
            wildCardNode.AssertNoWildCardNodes();
            wildCardNode.AssertNoLeafNodes();
            Assert.Equal(1, wildCardNode.WildCardDepth);

            var wildChild = wildCardNode.ChildNodes.First();
            wildChild.AssertPath("person/");
            wildChild.AssertNoLeafNodes();
            wildChild.AssertNoChildren();
            wildChild.WildCardNodes.AssertCount(1);
            Assert.Equal(1, wildChild.WildCardDepth);
            
            var secondWildNode = wildChild.WildCardNodes.First();
            secondWildNode.AssertNoChildren();
            secondWildNode.AssertNoWildCardNodes();
            secondWildNode.LeafNodes.AssertCount(1);

            var leafNode = secondWildNode.LeafNodes.First();
            
            Assert.Equal("GET", leafNode.Method);
            Assert.Equal("Person", leafNode.Value);

        }
    }
}
