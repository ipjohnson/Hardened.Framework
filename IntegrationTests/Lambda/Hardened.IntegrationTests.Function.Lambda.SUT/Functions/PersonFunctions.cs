using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Function.Lambda.Runtime;
using Hardened.IntegrationTests.Function.Lambda.SUT.Models;
using Hardened.IntegrationTests.Function.Lambda.SUT.Services;

namespace Hardened.IntegrationTests.Function.Lambda.SUT.Functions
{
    public class PersonFunctions
    {
        private IPersonService _personService;

        public PersonFunctions(IPersonService personService)
        {
            _personService = personService;
        }

        [LambdaFunction]
        public PersonListModel GetAllPeople()
        {
            return new PersonListModel("test", new List<PersonModel>());
        }
    }
}
