using Hardened.Requests.Abstract.Links;
using TestApp;

namespace TestApp
{
    /// <summary>
    /// The routes Application declares, as paths. For a link a client can call, use ApplicationLinks.
    /// </summary>
    public static class ApplicationRoutes
    {

        public static class Context
        {

            public static string WhoAmI()
            {
                return "/whoami";
            }
        }
    }

    /// <summary>
    /// Links to Application's routes, as a client would call them. Resolve from the container, or read it off a template.
    /// </summary>
    public sealed class ApplicationLinks
    {
        private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;
        private global::TestApp.ApplicationLinks.ContextLinks? _Context;

        public ApplicationLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
        {
            _context = context;
        }

        public global::TestApp.ApplicationLinks.ContextLinks Context => _Context ??= new ContextLinks(_context);

        public sealed class ContextLinks
        {
            private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;

            public ContextLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
            {
                _context = context;
            }

            public string WhoAmI()
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.Context.WhoAmI());
            }

            public string WhoAmIAbsolute()
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.Context.WhoAmI());
            }
        }
    }
}
