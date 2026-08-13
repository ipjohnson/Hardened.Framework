using Hardened.Web.AspNetCore.Runtime.Impl;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Hardened.Web.AspNetCore.Runtime.Tests.Impl;

/// <summary>
/// The status contract, which is subtler than it looks and was wrong for a long time.
///
/// <c>ResourceNotFoundHandler</c> supplies a 404 only when it finds the status still unset, so the
/// getter has to distinguish "nothing has decided yet" from "200". Reading
/// <c>HttpResponse.StatusCode</c> back — which ASP.NET initialises to 200 — collapses that
/// distinction and stops the handler ever firing.
/// </summary>
public class AspNetExecutionResponseTests {

    [Fact]
    public void Status_IsNullBeforeAnythingSetsIt() {
        var response = new AspNetExecutionResponse(new DefaultHttpContext().Response);

        Assert.Null(response.Status);
    }

    [Fact]
    public void Status_WritesThroughToTheHttpResponse() {
        var httpContext = new DefaultHttpContext();
        var response = new AspNetExecutionResponse(httpContext.Response);

        response.Status = 404;

        Assert.Equal(404, response.Status);
        Assert.Equal(404, httpContext.Response.StatusCode);
    }

    /// <summary>Clearing the status returns the response to the default rather than to zero.</summary>
    [Fact]
    public void Status_NullResetsTheHttpResponseToTwoHundred() {
        var httpContext = new DefaultHttpContext();
        var response = new AspNetExecutionResponse(httpContext.Response) { Status = 500 };

        response.Status = null;

        Assert.Null(response.Status);
        Assert.Equal(200, httpContext.Response.StatusCode);
    }

    /// <summary>
    /// Once the response has started the status is settled, so it is reported rather than left
    /// null. Without this every successful request logs a blank status at <c>RequestEnd</c>,
    /// because nothing sets a status on an ordinary success path.
    /// </summary>
    [Fact]
    public void Status_ReportsTheResponseStatusOnceTheResponseHasStarted() {
        var httpContext = StartableResponseContext.Create(
            Substitute.For<IServiceProvider>(), out var start);
        var response = new AspNetExecutionResponse(httpContext.Response);

        Assert.Null(response.Status);

        httpContext.Response.StatusCode = 201;
        start();

        Assert.Equal(201, response.Status);
    }

    [Fact]
    public void Clone_CarriesTheStatus() {
        var response = new AspNetExecutionResponse(new DefaultHttpContext().Response) { Status = 418 };

        var clone = response.Clone(null);

        Assert.Equal(418, clone.Status);
    }

    [Fact]
    public void Clone_LeavesAnUnsetStatusUnset() {
        var response = new AspNetExecutionResponse(new DefaultHttpContext().Response);

        var clone = response.Clone(null);

        Assert.Null(clone.Status);
    }
}
