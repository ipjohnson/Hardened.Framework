using Hardened.Requests.Abstract.Links;
using TestApp;

namespace TestApp
{
    /// <summary>
    /// The routes Application declares, as paths. For a link a client can call, use ApplicationLinks.
    /// </summary>
    public static class ApplicationRoutes
    {

        public static class Session
        {

            public static string Read()
            {
                return "/session";
            }
        }
    }

    /// <summary>
    /// Links to Application's routes, as a client would call them. Resolve from the container, or read it off a template.
    /// </summary>
    public sealed class ApplicationLinks
    {
        private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;
        private global::TestApp.ApplicationLinks.SessionLinks? _Session;

        public ApplicationLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
        {
            _context = context;
        }

        public global::TestApp.ApplicationLinks.SessionLinks Session => _Session ??= new SessionLinks(_context);

        public sealed class SessionLinks
        {
            private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;

            public SessionLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
            {
                _context = context;
            }

            public string Read()
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.Session.Read());
            }

            public string ReadAbsolute()
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.Session.Read());
            }
        }
    }
}
