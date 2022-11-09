﻿using Hardened.IntegrationTests.Web.Lambda.SUT.Filters;
using Hardened.IntegrationTests.Web.Lambda.SUT.Models;
using Hardened.IntegrationTests.Web.Lambda.SUT.Services;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Attributes;
using Microsoft.Extensions.Logging;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Controllers;

public class PersonController
{
    private readonly IPersonService _personService;
    private readonly ILogger<PersonController> _logger;

    public PersonController(IPersonService personService, ILogger<PersonController> logger)
    {
        _personService = personService;
        _logger = logger;
    }

    public const string TestMethodPath = "/api/person/TestMethod";

    [Get(TestMethodPath)]
    public PersonModel TestMethod([FromServices] ISomeTestService someTestService)
    {
        return new PersonModel { FirstName = someTestService.TestMethod(), Id = 10 };
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

    [Get("/api/person/{id}")]
    public PersonModel? Get(int id)
    {
        return _personService.Get(id);
    }

    [Get("/api/person/{id}/Get")]
    public PersonModel? Get2(int id)
    {
        var model = _personService.Get(id);

        if (model != null)
        {
            model.FirstName = "Get";
        }

        return model;
    }

    [Post("/api/person")]
    public PersonModel Add(PersonModel personModel)
    {
        return _personService.Add(personModel);
    }
}

