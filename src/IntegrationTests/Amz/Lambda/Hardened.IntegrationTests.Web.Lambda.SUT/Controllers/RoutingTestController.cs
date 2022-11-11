﻿using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Controllers;

public class RoutingTestController
{
    [Get("/company")]
    [AssignMethod]
    public Response CompanyGetAll()
    {
        return new Response
        {
            Company = ""
        };
    }

    [Get("/company/{company}")]
    [AssignMethod]
    public Response CompanyGet(string company)
    {
        return new Response
        {
            Company = company
        };
    }
    
    [Get("/company/{company}/Subscription")]
    [AssignMethod]
    public Response CompanyGetSubscription(string company)
    {
        return new Response { Company = company };
    }

    [Get("/company/{company}/Subscription/{id}")]
    [AssignMethod]
    public Response CompanyGetSubscriptionWithQuery(
        string company, 
        string id, 
        [FromQueryString]
        string queryParam = "unknown")
    {
        return new Response
        {
            Company = company,
            Id = id,
            QueryParam = queryParam
        };
    }

    public class Response
    {
        public string Company { get; init; } = default!;
        
        public string? Id { get; set; }
        
        public string? QueryParam { get; set; }

        public string? Method { get; set; }
    }

    public class AssignMethodAttribute : Attribute, IRequestFilterProvider, IExecutionFilter
    {
        public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo)
        {
            yield return new RequestFilterInfo(_ => this);
        }

        public async Task Execute(IExecutionChain chain)
        {
            await chain.Next();
            
            if (chain.Context.Response.ResponseValue is Response response)
            {
                response.Method = chain.Context.HandlerInfo?.InvokeMethod;
            }
        }
    }
}