using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Function.Lambda.Runtime.Impl;
using Hardened.Function.Lambda.Testing;
using Hardened.IntegrationTests.Function.Lambda.SUT.Functions;
using Hardened.IntegrationTests.Function.Lambda.SUT.Models;
using Hardened.Shared.Testing.Attributes;
using Xunit;

namespace Hardened.IntegrationTests.Function.Lambda.Tests.Functions
{
    public class PersonFunctionsTests
    {
        [HardenedTest]
        public async Task GetAllPeople(LambdaTestApp app)
        {
            var request = new PersonFunctions.GetAllPeopleRequest { MaxCount = 20 };

            var personListModel =
                await app.Invoke<PersonListModel>("GetAllPeople", request);

            Assert.NotNull(personListModel);
            Assert.Equal("test", personListModel.Title);
        }


        //[HardenedTest]
        //public async Task GetAllPeople(PersonFunctions_GetAllPeople lambda)
        //{
        //    var personListModel = await lambda.Invoke(new PersonFunctions.GetAllPeopleRequest { MaxCount = 20 });

        //    Assert.NotNull(personListModel);
        //    Assert.Equal("test", personListModel.Title);
        //}

        //[HardenedTest]
        //public async Task GetAllPeopleWithContext(PersonFunctions_GetAllPeople lambda)
        //{
        //    var personListModel = await lambda.Invoke(
        //        new PersonFunctions.GetAllPeopleRequest { MaxCount = 20 }, 
        //        customData => customData.Add("contextString", "contextString123"));

        //    Assert.NotNull(personListModel);
        //    //Assert.Equal("contextString123", personListModel.Title);
        //}
    }
}
