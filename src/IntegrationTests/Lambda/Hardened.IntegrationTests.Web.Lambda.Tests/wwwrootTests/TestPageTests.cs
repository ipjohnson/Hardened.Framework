using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.IntegrationTests.Web.Lambda.SUT.Models;
using Hardened.IntegrationTests.Web.Lambda.SUT.Services;
using Hardened.IntegrationTests.Web.Lambda.Tests.Extensions;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Testing;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Runtime.Configuration;
using Hardened.Web.Runtime.Headers;
using Hardened.Web.Testing;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.Web.Lambda.Tests.wwwrootTests
{
    public class TestPageTests
    {
        [HardenedTest]
        public async Task TestPage(ITestWebApp app)
        {
            var response = await app.Get("/testPage.html");

            response.Assert.Ok();

            var testPage = await response.ParseDocument();
            
            Assert.NotNull(testPage);
            Assert.Equal("gzip", response.Headers.Get(KnownHeaders.ContentEncoding));
        }

        [HardenedTest]
        public async Task TestPageWithNoEncoding(ITestWebApp app)
        {
            var response = await app.Get("/testPage.html", 
                request => request.Headers.Set(KnownHeaders.AcceptEncoding, (object?)null));

            response.Assert.Ok();

            var testPage = await response.ParseDocument();

            Assert.NotNull(testPage);
            Assert.Equal(StringValues.Empty, response.Headers.Get(KnownHeaders.ContentEncoding));
        }

        public void Configure(IAppConfig appConfig)
        {
            appConfig.Amend<StaticContentConfiguration>(c => c.CacheMaxAge = 20);
        }
    }
}
