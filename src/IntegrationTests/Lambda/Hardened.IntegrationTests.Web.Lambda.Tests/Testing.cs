﻿using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;

namespace Hardened.IntegrationTests.Web.Lambda.Tests
{
    public class Testing
    {
        [HardenedTest]
        public async Task SomeTest(ITestWebApp app)
        {
            var response = await app.Get("/home");
        }
    }
}
