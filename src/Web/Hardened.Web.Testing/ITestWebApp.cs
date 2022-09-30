namespace Hardened.Web.Testing
{

    public interface ITestWebApp
    {
        IServiceProvider RootServiceProvider { get; }

        /// <summary>
        /// Get method
        /// </summary>
        /// <param name="path">request path</param>
        /// <param name="webRequest">web request configuration</param>
        /// <returns></returns>
        Task<TestWebResponse> Get(string path, Action<TestWebRequest>? webRequest = null);

        /// <summary>
        /// Post value to path
        /// </summary>
        /// <param name="value"></param>
        /// <param name="path"></param>
        /// <param name="webRequest"></param>
        /// <returns></returns>
        Task<TestWebResponse> Post(object value, string path, Action<TestWebRequest>? webRequest = null);

        /// <summary>
        /// Put value to path
        /// </summary>
        /// <param name="value"></param>
        /// <param name="path"></param>
        /// <param name="webRequest"></param>
        /// <returns></returns>
        Task<TestWebResponse> Put(object value, string path, Action<TestWebRequest>? webRequest = null);

        /// <summary>
        /// Patch value to path
        /// </summary>
        /// <param name="value"></param>
        /// <param name="path"></param>
        /// <param name="webRequest"></param>
        /// <returns></returns>
        Task<TestWebResponse> Patch(object value, string path, Action<TestWebRequest>? webRequest = null);

        /// <summary>
        /// Delete path
        /// </summary>
        /// <param name="path"></param>
        /// <param name="webRequest"></param>
        /// <returns></returns>
        Task<TestWebResponse> Delete(string path, Action<TestWebRequest>? webRequest = null);

        /// <summary>
        /// Send HTTP request
        /// </summary>
        /// <param name="method"></param>
        /// <param name="value"></param>
        /// <param name="path"></param>
        /// <param name="webRequest"></param>
        /// <returns></returns>
        Task<TestWebResponse> Request(string method, object? value, string path, Action<TestWebRequest>? webRequest = null);

    }
}
