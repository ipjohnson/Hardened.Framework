using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Function.Lambda.Runtime.Attributes;
using Hardened.IntegrationTests.Function.Lambda.SUT.Models;

namespace Hardened.IntegrationTests.Function.Lambda.SUT.Functions
{
    public class PersonFunctions
    {
        [LambdaFunction]
        public PersonListModel GetAllPeople()
        {
            return new PersonListModel("test", new List<PersonModel>());
        }
    }
}
