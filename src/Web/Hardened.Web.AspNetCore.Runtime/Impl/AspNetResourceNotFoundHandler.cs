using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Web.AspNetCore.Runtime.Impl;

/// <summary>
/// The not-found handler for a host that has somewhere else to send the request.
/// </summary>
/// <remarks>
/// <para>
/// Every other host is terminal — Kestrel, the Lambda runtimes and the test harness each own the
/// whole response, so <c>ResourceNotFoundHandler</c> setting a 404 is the right and final answer.
/// Under ASP.NET Core, <c>UseHardened()</c> sits in a pipeline that may have static files, another
/// middleware or MVC behind it, and a path Hardened declares no route for is one of those things'
/// to answer.
/// </para>
/// <para>
/// So this one leaves the status alone. An unset status is what
/// <c>AspNetCoreRequestHandler</c> reads as "the chain did not answer this" before handing the
/// request on, and if nothing behind Hardened answers either, ASP.NET's own terminal 404 is what
/// the client gets — which is what an ASP.NET-hosted application already produced for a genuine
/// miss, so nothing observable changes for that case.
/// </para>
/// <para>
/// <c>chain.Next()</c> is still called, for the same reason the default calls it: filters ordered
/// after the routing filter have not run yet, and one of them may answer. If one does, it sets a
/// status and the request stops here rather than falling through.
/// </para>
/// </remarks>
public class AspNetResourceNotFoundHandler : IResourceNotFoundHandler {
    public Task Handle(IExecutionChain chain) {
        return chain.Next();
    }
}
