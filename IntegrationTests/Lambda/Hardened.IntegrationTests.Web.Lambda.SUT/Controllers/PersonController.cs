using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.IntegrationTests.Web.Lambda.SUT.Filters;
using Hardened.IntegrationTests.Web.Lambda.SUT.Models;
using Hardened.IntegrationTests.Web.Lambda.SUT.Services;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Attributes;
using Microsoft.Extensions.Logging;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Controllers
{
    public class PersonController
    {
        private readonly IPersonService _personService;
        private readonly ILogger<PersonController> _logger;

        public PersonController(IPersonService personService, ILogger<PersonController> logger)
        {
            _personService = personService;
            _logger = logger;
        }
        
        [Get("/api/person/")]
        [TestingFilter(TestValue = 10, OtherValue = 50)]
        public IEnumerable<PersonModel> GetAll()
        {
            return _personService.All();
        }

        [Get("/api/person/view")]
        [Template("personList")]
        public async Task<PersonListModel> GetAllWithView()
        {
            _logger.LogInformation("Person list is executing");
            return new PersonListModel("Person List", _personService.All());
        }

        //[Get("/api/person/{id}")]
        //public PersonModel? Get(int id)
        //{
        //    return _personService.Get(id);
        //}

        //[Put()]
        //public PersonModel Add(PersonModel personModel)
        //{
        //    return _personService.Add(personModel);
        //}
    }
}
