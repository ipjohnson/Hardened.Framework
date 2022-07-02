namespace Hardened.Web.Testing
{
    public delegate void ConfigureWebRequest(TestWebRequest request);

    public interface ITestWebApp
    {
        IServiceProvider RootServiceProvider { get; }

        Task<TestWebResponse> Get(string path, ConfigureWebRequest? webRequest = null);

        Task<TestWebResponse> Post(object postValue, string path, ConfigureWebRequest? webRequest = null);
    }
}
