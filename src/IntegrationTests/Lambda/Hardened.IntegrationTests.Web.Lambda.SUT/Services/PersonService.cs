using Hardened.IntegrationTests.Web.Lambda.SUT.Models;
using Hardened.Shared.Runtime.Attributes;
using Hardened.IntegrationTests.Web.Lambda.SUT.Configuration;
using Microsoft.Extensions.Options;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Services
{
    public interface IPersonService
    {
        IEnumerable<PersonModel> All();

        PersonModel? Get(int id);

        PersonModel Add(PersonModel person);
    }

    [Expose]
    public class PersonService : IPersonService
    {
        private IOptions<IPersonServiceConfiguration> _configuration;
        private readonly Dictionary<int, PersonModel> _persons;

        public PersonService(IOptions<IPersonServiceConfiguration> configuration)
        {
            _configuration = configuration;

            _persons = new()
            {
                { 10, new PersonModel { Id = 10, FirstName = _configuration.Value.FirstNamePrefix + "Test", LastName = _configuration.Value.LastNamePrefix + "Test"} },
                { 20, new PersonModel { Id = 20, FirstName = _configuration.Value.FirstNamePrefix + "Test 20", LastName = _configuration.Value.LastNamePrefix + "LastTest20" } },
                { 30, new PersonModel { Id = 30, FirstName = _configuration.Value.FirstNamePrefix + "Test 30", LastName = _configuration.Value.LastNamePrefix + "LastTest30" } },
                { 40, new PersonModel { Id = 40, FirstName = _configuration.Value.FirstNamePrefix + "Test 40", LastName = _configuration.Value.LastNamePrefix + "LastTest40" } },
                { 50, new PersonModel { Id = 40, FirstName = _configuration.Value.FirstNamePrefix + "Test 40", LastName = _configuration.Value.LastNamePrefix + "LastTest40" } },
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
            person.FirstName = _configuration.Value.FirstNamePrefix + person.FirstName;
            person.LastName = _configuration.Value.LastNamePrefix + person.LastName;

            _persons[person.Id] = person;
            return person;
        }
    }
}
