using Hardened.IntegrationTests.Function.Lambda.SUT.Configuration;
using Hardened.IntegrationTests.Function.Lambda.SUT.Models;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.Options;

namespace Hardened.IntegrationTests.Function.Lambda.SUT.Services;

public interface IPersonService
{
    IEnumerable<PersonModel> All();

    PersonModel? Get(int id);

    PersonModel Add(PersonModel person);
}

[Expose]
public class FunctionPersonService : IPersonService
{
    private readonly Dictionary<int, PersonModel> _persons;

    public FunctionPersonService(IOptions<IPersonServiceConfiguration> configuration)
    {

        _persons = new()
        {
            { 10, new PersonModel { Id = 10, FirstName = configuration.Value.FirstNamePrefix + "Test", LastName = configuration.Value.LastNamePrefix + "Test" } },
            { 20, new PersonModel { Id = 20, FirstName = configuration.Value.FirstNamePrefix + "Test 20", LastName = configuration.Value.LastNamePrefix + "LastTest20" } },
            { 30, new PersonModel { Id = 30, FirstName = configuration.Value.FirstNamePrefix + "Test 30", LastName = configuration.Value.LastNamePrefix + "LastTest30" } },
            { 40, new PersonModel { Id = 40, FirstName = configuration.Value.FirstNamePrefix + "Test 40", LastName = configuration.Value.LastNamePrefix + "LastTest40" } }
        };
    }

    public IEnumerable<PersonModel> All()
    {
        return _persons.Values;
    }

    public PersonModel? Get(int id)
    {
        _persons.TryGetValue(id, out var returnModel);

        return returnModel;
    }

    public PersonModel Add(PersonModel person)
    {
        _persons[person.Id] = person;
        return person;
    }
}