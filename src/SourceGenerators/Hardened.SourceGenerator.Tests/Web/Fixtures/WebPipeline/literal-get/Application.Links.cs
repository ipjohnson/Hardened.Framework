using Hardened.Requests.Abstract.Links;
using TestApp;

namespace TestApp
{
    /// <summary>
    /// The routes Application declares, as paths. For a link a client can call, use ApplicationLinks.
    /// </summary>
    public static class ApplicationRoutes
    {

        public static class Home
        {

            public static string Hello()
            {
                return "/hello";
            }
        }
    }

    /// <summary>
    /// Links to Application's routes, as a client would call them. Resolve from the container, or read it off a template.
    /// </summary>
    public sealed class ApplicationLinks
    {
        private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;
        private global::TestApp.ApplicationLinks.HomeLinks? _Home;

        public ApplicationLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
        {
            _context = context;
        }

        public global::TestApp.ApplicationLinks.HomeLinks Home => _Home ??= new HomeLinks(_context);

        public sealed class HomeLinks
        {
            private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;

            public HomeLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
            {
                _context = context;
            }

            public string Hello()
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.Home.Hello());
            }

            public string HelloAbsolute()
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.Home.Hello());
            }
        }
    }
}
