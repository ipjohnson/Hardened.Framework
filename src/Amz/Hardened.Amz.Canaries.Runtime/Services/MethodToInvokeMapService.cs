using Hardened.Amz.Canaries.Runtime.Models.Request;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Canaries.Runtime.Services;

public interface IMethodToInvokeMapService
{
    void CreateMapWithData(IReadOnlyList<InvokeRequestModel> requestModels);

    InvokeRequestModel GetInvokeRequest(string className, string methodName);
}

[Expose]
[Singleton]
public class MethodToInvokeMapService : IMethodToInvokeMapService
{
    private Dictionary<string, InvokeRequestModel>? _requestModels;

    public void CreateMapWithData(IReadOnlyList<InvokeRequestModel> requestModels)
    {
        _requestModels = new Dictionary<string, InvokeRequestModel>();

        foreach (var invokeRequestModel in requestModels)
        {
            var key = TestKey(
                invokeRequestModel.Definition.TestClassName,
                invokeRequestModel.Definition.TestMethod);
            
            _requestModels[key] = invokeRequestModel;
        }
    }

    public InvokeRequestModel GetInvokeRequest(string className, string methodName)
    {
        var key = TestKey(className, methodName);

        if (_requestModels!.TryGetValue(key, out var model))
        {
            return model;
        }

        throw new Exception($"Could not find request for {className} method {methodName}");
    }

    private string TestKey(string className, string methodName)
    {
        return $"{className}|{methodName}";

    }
}