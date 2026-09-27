using Hardened.Requests.Abstract.Errors;
using Microsoft.AspNetCore.Http;

namespace Hardened.Web.AspNetCore.Runtime;

/// <summary>
/// The status ASP.NET Core's server put on a request it refused while the pipeline read it: 413 for
/// a body over the server's limit, 400 for one that ended early.
/// </summary>
public sealed class BadHttpRequestStatusReader : IExceptionStatusReader
{
    public int? StatusOf(Exception exception) =>
        exception is BadHttpRequestException refused ? refused.StatusCode : null;
}
