using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Hardened.IntegrationTests.Web.Lambda.SUT.Models;
using Hardened.IntegrationTests.Web.Lambda.SUT.Services;
using Hardened.IntegrationTests.Web.Lambda.Tests.Extensions;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing;
using Hardened.Web.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.IntegrationTests.Web.Lambda.Tests.IntegrationTests
{
    public class PersonTests
    {
        [Theory]
        [WebIntegration]
        public async Task SomeTest(ITestWebApp app)
        {
            var testWebResponse = await app.Get("/api/person/testMethod");

            testWebResponse.Assert.Ok();
            var model = testWebResponse.Deserialize<PersonModel>();

            Assert.NotNull(model);
            Assert.Equal(10, model.Id);
            Assert.Equal("test string", model.FirstName);
        }

        [Theory]
        [WebIntegration]
        [TestExposeMethod(nameof(ExposeMethod))]
        [EnvironmentValue("Testing", "Value")]
        [EnvironmentName("SomeEnv")]
        public async Task PersonWebPageTest(ITestWebApp app)
        {
            var viewResponse = await app.Get("/api/person/view");

            viewResponse.Assert.Ok();
            var document = await viewResponse.ParseDocument();

            var results = document.QuerySelector("#id5");

            Assert.NotNull(results);
            Assert.Equal("TestValue", viewResponse.Headers.Get("TestResponseHeader"));
            Assert.Equal("OtherValue", viewResponse.Headers.Get("OtherTest"));
        }

        public static void ExposeMethod(MethodInfo testMethod, IServiceCollection collection)
        {
            collection.AddSingleton<IPersonService, TestPersonService>();
        }

        public class TestPersonService : IPersonService
        {
            private IEnvironment _environment;

            public TestPersonService(IEnvironment environment)
            {
                _environment = environment;
            }

            public IEnumerable<PersonModel> All()
            {
                return new[] { new PersonModel { Id = 5, FirstName = "Test", LastName = "Testing" } };
            }

            public PersonModel? Get(int id)
            {
                return null;
            }

            public PersonModel Add(PersonModel person)
            {
                return person;
            }
        }
    }
}
