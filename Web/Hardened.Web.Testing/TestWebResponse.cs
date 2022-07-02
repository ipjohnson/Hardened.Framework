using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.Web.Testing
{

    public class TestWebResponse
    {
        private readonly IExecutionResponse _executionResponse;
        private IWebAssertThat? _assertThat;
        public TestWebResponse(IExecutionResponse executionResponse)
        {
            _executionResponse = executionResponse;
        }

        public int StatusCode => _executionResponse.Status.GetValueOrDefault(200);

        public IHeaderCollection Headers => _executionResponse.Headers;

        public Stream Body => _executionResponse.Body;

        public IWebAssertThat Assert => _assertThat ??= new WebAssertThat(this);

        public T Deserialize<T>()
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(Body) ??
                   throw new Exception("Could not deserialize response");
        }
    }
}
