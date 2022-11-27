using Amazon.Lambda.Core;
using Hardened.Amz.Canaries.Runtime.DynamoDb;
using Hardened.Amz.Canaries.Runtime.Models.Request;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Xunit.Runners;
using Exception = System.Exception;

namespace Hardened.Amz.Canaries.Runtime.Services;


public interface IXUnitMonitoringService
{
    void SetupLogging(AssemblyRunner runner);

    Task Flush();
}

[Expose]
[Singleton]
public class XUnitMonitoringService : IXUnitMonitoringService
{
    private readonly ConcurrentDictionary<string, List<Tuple<string, DateTime>>> _logs = new();
    private readonly IMethodToInvokeMapService _methodToInvokeMap;
    private readonly ICanaryDynamoWriter _canaryDynamoWriter;
    private readonly ICloudWatchMetricsService _metricsService;
    private readonly ILogger<XUnitMonitoringService> _logger;
    private readonly ICloudWatchLogStreamWriter _logStreamWriter;
    private readonly List<Task> _pendingTasks = new();
    
    public XUnitMonitoringService(ICloudWatchLogStreamWriter logStreamWriter,
        IMethodToInvokeMapService methodToInvokeMap,
        ICanaryDynamoWriter canaryDynamoWriter,
        ICloudWatchMetricsService metricsService, 
        ILogger<XUnitMonitoringService> logger)
    {
        _logStreamWriter = logStreamWriter;
        _methodToInvokeMap = methodToInvokeMap;
        _logger = logger;
        _metricsService = metricsService;
        _canaryDynamoWriter = canaryDynamoWriter;
    }

    public void SetupLogging(AssemblyRunner runner)
    {
        runner.OnTestStarting += TestStartingHandler;
        runner.OnTestOutput += TestOutputHandler;
        runner.OnTestPassed += TestPassedHandler;
        runner.OnTestFailed += TestFailedHandler;
    }

    public async Task Flush()
    {
        try
        {
            var keepRunning = true;
            
            while (keepRunning)
            {
                keepRunning = false;

                foreach (var pendingTask in _pendingTasks)
                {
                    if (!pendingTask.IsCompleted)
                    {
                        keepRunning = true;
                    }
                }

                if (keepRunning)
                {
                    await Task.Delay(1000);
                }
            }

            await Task.WhenAll(_pendingTasks);
        }
        catch (Exception exp)
        {
            _logger.LogInformation(exp, "Exception thrown while waiting for pending tasks");
        }
        finally
        {
            _pendingTasks.Clear();
        }
    }

    private void TestFailedHandler(TestFailedInfo info)
    {
        HandleFinishedTest(
            info.TypeName,
            info.MethodName, 
            false, 
            TimeSpan.FromSeconds(Convert.ToDouble(info.ExecutionTime)));
    }

    private void TestPassedHandler(TestPassedInfo info)
    {
        HandleFinishedTest(
            info.TypeName,
            info.MethodName, 
            true, 
            TimeSpan.FromSeconds(Convert.ToDouble(info.ExecutionTime)));
    }
    
    private void HandleFinishedTest(string className, string methodName, bool success, TimeSpan duration)
    {
        var invokeRequestModel = _methodToInvokeMap.GetInvokeRequest(className, methodName);

        if (_logs.TryGetValue(invokeRequestModel.InvokeId, out var logStatementList))
        {
            logStatementList.Add(
                new Tuple<string, DateTime>(
                    $"Finished test class {className} method {methodName}",
                    DateTime.Now
                ));

            _pendingTasks.Add(Task.Run(
                () => ReportResulLogsAndMetrics(success, duration, invokeRequestModel, logStatementList)));
        }
    }

    private async Task ReportResulLogsAndMetrics(
        bool success,
        TimeSpan duration,
        InvokeRequestModel invokeRequestModel,
        List<Tuple<string, DateTime>> logStatementList)
    {   
        var metricString =
            _metricsService.GetMetricString(invokeRequestModel.CanaryName, success, duration);
     
        Console.WriteLine(metricString);
        
        try
        {
            await _logStreamWriter.WriteLogStatements(
                invokeRequestModel.CanaryName,
                invokeRequestModel.InvokeId,
                logStatementList
            );
        }
        catch (Exception exp)
        {
            _logger.LogError(exp, "Exception thrown while writing log statements");
        }

        try
        {
            await _canaryDynamoWriter.WriteCanaryResult(
                invokeRequestModel.CanaryName,
                invokeRequestModel.Definition,
                invokeRequestModel.InvokeId,
                success, 
                duration
            );
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private void TestStartingHandler(TestStartingInfo info)
    {
        var invokeRequestModel = _methodToInvokeMap.GetInvokeRequest(info.TypeName, info.MethodName);

        var list = new List<Tuple<string, DateTime>>();
        _logs[invokeRequestModel.InvokeId] = list;

        list.Add(
            new Tuple<string, DateTime>(
                $"Starting test class {info.TypeName} method {info.MethodName}",
                DateTime.Now
            ));
    }

    private void TestOutputHandler(TestOutputInfo info)
    {
        var invokeRequestModel = _methodToInvokeMap.GetInvokeRequest(info.TypeName, info.MethodName);

        if (_logs.TryGetValue(invokeRequestModel.InvokeId, out var logStatementList))
        {
            logStatementList.Add(new Tuple<string, DateTime>(
                info.Output,
                DateTime.Now
            ));
        }
    }
}