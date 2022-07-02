using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Web.Testing;
using Xunit;

namespace Hardened.IntegrationTests.Web.Lambda.Tests.IntegrationTests
{
    public class PersonTests
    {
        [Theory]
        [WebIntegration]
        public async Task PersonWebPageTest(ITestWebApp app)
        {
            var viewResponse = await app.Get("/api/person/view");

            viewResponse.Assert.Ok();
            var streamReader = new StreamReader(viewResponse.Body);
            var fullString = await streamReader.ReadToEndAsync();
        }
    }
}
