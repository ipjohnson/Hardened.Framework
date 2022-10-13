using Hardened.Function.Lambda.Runtime;
using Hardened.IntegrationTests.Function.Lambda.SUT.Models;
using Hardened.IntegrationTests.Function.Lambda.SUT.Services;
using Hardened.Requests.Abstract.Attributes;

namespace Hardened.IntegrationTests.Function.Lambda.SUT.Functions;

public class PersonFunctions
{
    private readonly IPersonService _personService;

    public PersonFunctions(IPersonService personService)
    {
        _personService = personService;
    }

    [HardenedFunction("some-test-function")]
    public async Task<PersonModel> Test()
    {
        return new PersonModel();
    }

    [HardenedFunction]
    public async Task<PersonListModel> GetAllPeople(GetAllPeopleRequest request, [FromContext] string contextString = "test")
    {
        return new PersonListModel(contextString, new List<PersonModel>());
    }

    public class GetAllPeopleRequest
    {
        public int MaxCount { get; set; } = 10;
    }
}