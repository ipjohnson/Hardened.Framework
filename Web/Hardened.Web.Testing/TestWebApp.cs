using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.Headers;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Web.Runtime.Headers;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Testing
{

    public class TestWebApp : ITestWebApp
    {
        private readonly IApplicationRoot _applicationRoot;

        public TestWebApp(IApplicationRoot applicationRoot)
        {
            _applicationRoot = applicationRoot;
        }

        public IServiceProvider RootServiceProvider => _applicationRoot.Provider;

        public Task<TestWebResponse> Get(string path, ConfigureWebRequest? webRequest = null)
        {
            return ExecuteHttpMethod("GET", path, webRequest);
        }
        
        public Task<TestWebResponse> Post(object postValue, string path, ConfigureWebRequest? webRequest = null)
        {
            return ExecuteHttpMethod("POST", path, webRequest, postValue);
        }

        private async Task<TestWebResponse> ExecuteHttpMethod(string httpMethod, string path, ConfigureWebRequest? webRequest, object? bodyValue = null)
        {
            var startTimestamp = MachineTimestamp.Now;

            var middlewareService = _applicationRoot.Provider.GetRequiredService<IMiddlewareService>();
            var requestLogger = _applicationRoot.Provider.GetRequiredService<IRequestLogger>();

            var responseBody = new MemoryStream();
            var scope = _applicationRoot.Provider.CreateScope();

            var context = CreateContext(httpMethod, path, webRequest, responseBody, scope);
            
            context.Request.Headers.Set(KnownHeaders.AcceptEncoding, "gzip");
            
            if (bodyValue != null && string.IsNullOrEmpty(context.Request.ContentType))
            {
                context.Request.Headers.Set(KnownHeaders.ContentType, "application/json");
            }

            webRequest?.Invoke(new TestWebRequest{ Headers = context.Request.Headers });

            context.Request.Body = SetupBodyStream(bodyValue);

            var chain = middlewareService.GetExecutionChain(context);

            requestLogger.RequestBegin(context);

            try
            {
                await chain.Next();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            scope.Dispose();

            responseBody.Position = 0;
            
            context.RequestMetrics.Record(RequestMetrics.TotalRequestDuration, startTimestamp.GetElapsedMilliseconds());
            requestLogger.RequestEnd(context);

            return new TestWebResponse(context.Response);
        }

        private Stream SetupBodyStream(object? bodyValue)
        {
            if (bodyValue == null)
                return Stream.Null;

            var memoryStream=  new MemoryStream();

            JsonSerializer.Serialize(memoryStream, bodyValue);

            memoryStream.Position = 0;

            return memoryStream;
        }

        private IExecutionContext CreateContext(string httpMethod, string path, ConfigureWebRequest? webRequest,
            MemoryStream responseBody, IServiceScope serviceScope)
        {
            var header = new HeaderCollectionStringValues();

            var pathMinusQuery = path;
            var questionMark = path.IndexOf('?');
            if (questionMark > -1)
            {
                pathMinusQuery = path.Substring(0, questionMark);
            }
            var request = new TestExecutionRequest(httpMethod, pathMinusQuery, "", ParseQueryStringFromPath(path)) { Headers = header };
            var response = new TestExecutionResponse(responseBody) { Headers = new HeaderCollectionStringValues() };

            return new TestExecutionContext(
                _applicationRoot.Provider, 
                serviceScope.ServiceProvider, 
                serviceScope.ServiceProvider.GetRequiredService<IKnownServices>(), 
                request,
                response);
        }

        private IQueryStringCollection ParseQueryStringFromPath(string path)
        {
            var questionMarkIndex = path.IndexOf('?');

            if (questionMarkIndex == -1 || questionMarkIndex == path.Length)
            {
                return EmptyQueryStringCollection.Instance;
            }

            var queryString = path.Substring(questionMarkIndex + 1);

            var queryStringValues = new Dictionary<string, string>();
            var pairs = queryString.Split('&');

            foreach (var kvp in pairs)
            {
                var values = kvp.Split('=');

                if (values.Length == 2)
                {
                    queryStringValues[values[0]] = values[1];
                }
                else
                {
                    queryStringValues[kvp] = "";
                }
            }

            return new SimpleQueryStringCollection(queryStringValues);
        }
    }
}
