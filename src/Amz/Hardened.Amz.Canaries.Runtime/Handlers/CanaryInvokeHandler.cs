using Hardened.Amz.Canaries.Runtime.Models;
using Hardened.Requests.Abstract.Attributes;
using System.Reflection;
using Xunit.Runners;

namespace Hardened.Amz.Canaries.Runtime.Handlers;

public class CanaryInvokeHandler
{
    [HardenedFunction("canary-invoke-handler")]
    public async Task<InvokeResponseModel> InvokeCanary(InvokeRequestModel requestModel)
    {
        using var runner =
            AssemblyRunner.WithoutAppDomain(Assembly.GetEntryAssembly()!.FullName);
        
        runner.Start();
        
        return new InvokeResponseModel();
    }
}