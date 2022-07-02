using Xunit;

namespace Hardened.Web.Testing
{

    public class WebAssertThat : IWebAssertThat
    {
        private readonly TestWebResponse _response;

        public WebAssertThat(TestWebResponse response)
        {
            _response = response;
        }

        public void Ok()
        {
            Assert.InRange(_response.StatusCode, 200, 299);
        }

        public void NotFound()
        {
            Assert.Equal(404, _response.StatusCode);
        }

        public void BadRequest()
        {
            Assert.Equal(400, _response.StatusCode);
        }

        public void Unauthorized()
        {
            Assert.Equal(401, _response.StatusCode);
        }

        public void Forbidden()
        {
            Assert.Equal(403, _response.StatusCode);
        }
    }
}
