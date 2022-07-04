using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.IntegrationTests.Web.Lambda.SUT.Models;
using Hardened.Shared.Runtime.Attributes;

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
        private readonly Dictionary<int, PersonModel> _persons = new()
        {
            { 10, new PersonModel { Id = 10, FirstName = "Test", LastName = "LastTest" } },
            { 20, new PersonModel { Id = 20, FirstName = "Test 20", LastName = "LastTest20" } },
            { 30, new PersonModel { Id = 30, FirstName = "Test 30", LastName = "LastTest30" } },
            { 40, new PersonModel { Id = 40, FirstName = "Test 40", LastName = "LastTest40" } }
        };

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
}
