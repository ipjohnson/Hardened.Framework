using Hardened.Amz.Canaries.Runtime.DynamoDb;
using Hardened.Amz.Canaries.Runtime.Models.Request;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Templates.Abstract;
using Microsoft.Extensions.Logging;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using Xunit.Runners;

namespace Hardened.Amz.Canaries.Runtime.Services;

public interface IXUnitInvokeService
{
    Task InvokeTests(List<InvokeRequestModel> requestModelList);
}

[Expose]
[Singleton]
public class XUnitInvokeService : IXUnitInvokeService
{
    private IXunitAssemblyUnderTest _aut;
    private ICanaryDynamoWriter _canaryDynamoWriter;
    private IXUnitMonitoringService _xUnitLoggingService;
    private IMethodToInvokeMapService _methodToInvokeMap;
    private ILogger<XUnitInvokeService> _logger;

    public XUnitInvokeService(IXunitAssemblyUnderTest aut,
        ICanaryDynamoWriter canaryDynamoWriter,
        IXUnitMonitoringService xUnitLoggingService,
        IMethodToInvokeMapService methodToInvokeMap,
        ILogger<XUnitInvokeService> logger)
    {
        _aut = aut;
        _logger = logger;
        _methodToInvokeMap = methodToInvokeMap;
        _xUnitLoggingService = xUnitLoggingService;
        _canaryDynamoWriter = canaryDynamoWriter;
    }

    public async Task InvokeTests(List<InvokeRequestModel> requestModelList)
    {
        var startedList = new List<InvokeRequestModel>();
        
        foreach (var requestModel in requestModelList)
        {
            if (await _canaryDynamoWriter.WriteCanaryStart(
                    requestModel.CanaryName,
                    requestModel.Definition,
                    requestModel.InvokeId))
            {
                startedList.Add(requestModel);
            }    
        }
        
        _methodToInvokeMap.CreateMapWithData(startedList);
        
        using var runner =
            AssemblyRunner.WithoutAppDomain(_aut.AssemblyUnderTest.Location);

        runner.TestCaseFilter = 
            testCase => TestCaseInStartedList(testCase, startedList);

        var task = SetupTaskSource(runner);

        _xUnitLoggingService.SetupLogging(runner);

        var now = MachineTimestamp.Now;
        
        runner.Start(parallel: true, maxParallelThreads: startedList.Count);
        
        try
        {
            await task;
        }
        catch (Exception exp)
        {
            _logger.LogError(exp, "Exception thrown while executing test");
        }

        await _xUnitLoggingService.Flush();
    }

    private bool TestCaseInStartedList(ITestCase testCase, List<InvokeRequestModel> startedList)
    {
        return startedList.Any(
            model =>  model.Definition.TestClassName == testCase.TestMethod.TestClass.Class.Name &&
                      model.Definition.TestMethod == testCase.TestMethod.Method.Name);
    }

    private Task SetupTaskSource(AssemblyRunner runner)
    {
        var taskSource = new TaskCompletionSource<bool>();

        runner.OnExecutionComplete += info =>
        {
            taskSource.SetResult(info.TestsFailed == 0);
        };

        return taskSource.Task;
    }
}