using Hardened.Amz.Canaries.Runtime.Models.Dashboards;
using Hardened.Amz.Canaries.Runtime.Models.Flight;
using Hardened.Amz.Canaries.Runtime.Services;
using Hardened.Amz.Function.Lambda.Testing;
using Hardened.Shared.Testing.Attributes;
using Hardened.Templates.Abstract;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Canaries.Runtime.Tests.Templates;

public class MainTemplateTests
{
    /*[HardenedTest]
    [CanaryDatabase]
    public async Task Test(LambdaTestApp testApp)
    {
        var value = await testApp.Invoke("canary-cloud-watch-dashboard", new { });
        using var streamRead = new StreamReader(value);
        var outputString = await streamRead.ReadToEndAsync();
        
        
    }

    private void RegisterDependencies(IServiceCollection collection)
    {
        collection.AddSingleton<IXunitAssemblyUnderTest>(new XunitAssemblyUnderTest(GetType().Assembly));
    }*/
    
    [HardenedTest]
    public async Task SinglePage(ITemplateExecutionService templateExecutionService, IServiceProvider serviceProvider)
    {
        var canaryDefinitions = CreateMainPageResponse();
        
        var htmlResponse = await templateExecutionService.Execute("main", canaryDefinitions, serviceProvider);
    }

    private DashboardMainPageResponseModel CreateMainPageResponse()
    {
        var testCanaryDefinition = new CanaryDefinition(
        "test-class",
        "test-method",
        new CanaryFrequency(1, CanaryFrequencyUnit.Minute, CanaryFlightStyle.Strict, false),
        true
        );

        var recentFlights = new List<CanaryFlightInfo>
        {
            new (
                "123", 
                FlightStatus.Passed, 
                DateTime.Now.AddMinutes(-1),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(25)),
            
            new (
                "123", 
                FlightStatus.Passed, 
                DateTime.Now.AddMinutes(-20),
                TimeSpan.FromSeconds(20),
                TimeSpan.FromSeconds(25))
        };
        
        return new DashboardMainPageResponseModel(
            new PaginationInfo(1, 10,1 ),
            new List<DashboardCanaryInstance>
            {
                new DashboardCanaryInstance("test-canary", 100, 80, testCanaryDefinition,recentFlights)
            }
        );
    }
}