using Amazon.Lambda.SQSEvents;
using Hardened.Amz.Canaries.Runtime.Models;
using Hardened.Amz.Canaries.Runtime.Models.Request;
using Hardened.Amz.Canaries.Runtime.Services;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Shared.Runtime.Json;
using System.Reflection;
using Xunit.Runners;

namespace Hardened.Amz.Canaries.Runtime.Handlers;

public class CanaryInvokeHandler
{
    public const string FunctionName = "canary-invoke-handler";
    private IXUnitInvokeService _xUnitInvokeService;
    private IJsonSerializer _serializer;
    
    public CanaryInvokeHandler(IXUnitInvokeService xUnitInvokeService, IJsonSerializer serializer)
    {
        _xUnitInvokeService = xUnitInvokeService;
        _serializer = serializer;
    }

    [HardenedFunction(FunctionName)]
    public async Task<SQSBatchResponse> InvokeCanary(SQSEvent sqsEvent)
    {
        var requestModelList = new List<InvokeRequestModel>();
        
        foreach (var message in sqsEvent.Records)
        {
            var requestModel = _serializer.Deserialize<InvokeRequestModel>(
                message.Body
            );
            
            requestModelList.Add(requestModel);
        }
        
        
        await _xUnitInvokeService.InvokeTests(requestModelList);

        return new SQSBatchResponse();
    }
}