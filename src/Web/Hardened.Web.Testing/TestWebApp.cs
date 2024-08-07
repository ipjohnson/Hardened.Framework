﻿using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.Headers;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Testing;

public class TestWebApp : TestContext, ITestWebApp {
    private readonly IApplicationRoot _applicationRoot;
    private readonly TestCancellationToken _testCancellationToken;

    public TestWebApp(IApplicationRoot applicationRoot, ILogger logger)
        : base(applicationRoot.Provider.GetRequiredService<TestCancellationToken>().Token, logger) {
        _applicationRoot = applicationRoot;
        _testCancellationToken = _applicationRoot.Provider.GetService<TestCancellationToken>()!;
    }

    public IServiceProvider RootServiceProvider => _applicationRoot.Provider;

    public Task<TestWebResponse> Get(string path, Action<TestWebRequest>? webRequest = null) {
        return ExecuteHttpMethod("GET", path, webRequest);
    }

    public Task<TestWebResponse> Post(object postValue, string path, Action<TestWebRequest>? webRequest = null) {
        return ExecuteHttpMethod("POST", path, webRequest, postValue);
    }

    public Task<TestWebResponse> Put(object value, string path, Action<TestWebRequest>? webRequest = null) {
        return ExecuteHttpMethod("PUT", path, webRequest, value);
    }

    public Task<TestWebResponse> Patch(object value, string path, Action<TestWebRequest>? webRequest = null) {
        return ExecuteHttpMethod("PATCH", path, webRequest, value);
    }

    public Task<TestWebResponse> Delete(string path, Action<TestWebRequest>? webRequest = null) {
        return ExecuteHttpMethod("DELETE", path, webRequest, null);
    }

    public Task<TestWebResponse> Request(string method, object? value, string path,
        Action<TestWebRequest>? webRequest = null) {
        return ExecuteHttpMethod(method, path, webRequest, value);
    }

    private async Task<TestWebResponse> ExecuteHttpMethod(string httpMethod, string path,
        Action<TestWebRequest>? webRequest, object? bodyValue = null) {
        _testCancellationToken.Token.ThrowIfCancellationRequested();

        var startTimestamp = MachineTimestamp.Now;

        var middlewareService = _applicationRoot.Provider.GetRequiredService<IMiddlewareService>();
        var requestLogger = _applicationRoot.Provider.GetRequiredService<IRequestLogger>();

        var responseBody = new MemoryStream();
        var scope = _applicationRoot.Provider.CreateScope();

        var context = CreateContext(
            httpMethod, path, webRequest, responseBody, scope);

        context.Request.Headers[KnownHeaders.AcceptEncoding]= KnownEncoding.GZip;

        if (bodyValue != null && string.IsNullOrEmpty(context.Request.ContentType)) {
            context.Request.Headers[KnownHeaders.ContentType] = KnownContentType.Js;
        }

        webRequest?.Invoke(new TestWebRequest { Headers = context.Request.Headers });

        context.Request.Body = SetupBodyStream(bodyValue);

        var chain = middlewareService.GetExecutionChain(context);

        requestLogger.RequestBegin(context);

        try {
            await chain.Next();
        }
        catch (Exception e) {
            Console.WriteLine(e);
            throw;
        }

        scope.Dispose();

        responseBody.Position = 0;

        context.RequestMetrics.Record(RequestMetrics.TotalRequestDuration, startTimestamp.GetElapsedMilliseconds());
        requestLogger.RequestEnd(context);

        return new TestWebResponse(context.Response);
    }

    private Stream SetupBodyStream(object? bodyValue) {
        if (bodyValue == null)
            return Stream.Null;

        var memoryStream = new MemoryStream();

        JsonSerializer.Serialize(memoryStream, bodyValue);

        memoryStream.Position = 0;

        return memoryStream;
    }

    private IExecutionContext CreateContext(
        string httpMethod,
        string path,
        Action<TestWebRequest>? webRequest,
        MemoryStream responseBody,
        IServiceScope serviceScope) {
        var header = new Dictionary<string, StringValues>();

        var testWebRequest = new TestWebRequest { Headers = header };

        webRequest?.Invoke(testWebRequest);

        testWebRequest.Token ??= _testCancellationToken.Token;

        var pathMinusQuery = path;
        var questionMark = path.IndexOf('?');
        if (questionMark > -1) {
            pathMinusQuery = path.Substring(0, questionMark);
        }

        var request =
            new TestExecutionRequest(httpMethod, pathMinusQuery, "", ParseQueryStringFromPath(path)) {
                Headers = header
            };
        var response = new TestExecutionResponse(responseBody) { Headers = header };

        return new TestExecutionContext(
            _applicationRoot.Provider,
            serviceScope.ServiceProvider,
            serviceScope.ServiceProvider.GetRequiredService<IKnownServices>(),
            request,
            response,
            testWebRequest.Token.Value);
    }

    private IQueryStringCollection ParseQueryStringFromPath(string path) {
        var questionMarkIndex = path.IndexOf('?');

        if (questionMarkIndex == -1 || questionMarkIndex == path.Length) {
            return EmptyQueryStringCollection.Instance;
        }

        var queryString = path.Substring(questionMarkIndex + 1);

        var queryStringValues = new Dictionary<string, string>();
        var pairs = queryString.Split('&');

        foreach (var kvp in pairs) {
            var values = kvp.Split('=');

            if (values.Length == 2) {
                queryStringValues[values[0]] = values[1];
            }
            else {
                queryStringValues[kvp] = "";
            }
        }

        return new SimpleQueryStringCollection(queryStringValues);
    }
}