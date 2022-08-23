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
        private readonly IPersonService _personService;

        public PersonFunctions(IPersonService personService)
        {
            _personService = personService;
        }
        
        [LambdaFunction]
        public PersonListModel GetAllPeople(GetAllPeopleRequest request, [FromContext] string? contextString = null)
        {
            contextString ??= "test";

            return new PersonListModel(contextString, new List<PersonModel>());
        }
        


        public class GetAllPeopleRequest
        {
            public int MaxCount { get; set; } = 10;
        }
    }
}
