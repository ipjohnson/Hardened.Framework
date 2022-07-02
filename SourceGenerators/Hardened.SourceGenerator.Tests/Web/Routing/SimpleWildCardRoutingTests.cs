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
                new ("/api/Person/{id}", "GET", "Person"),
            };

            var generator = new RouteTreeGenerator<string>();

            var routeTree = generator.GenerateTree(routes);
        }
    }
}
