using Hardened.Requests.Abstract.Links;
using TestApp;

namespace TestApp
{
    /// <summary>
    /// The routes Application declares, as paths. For a link a client can call, use ApplicationLinks.
    /// </summary>
    public static class ApplicationRoutes
    {

        public static class Clock
        {

            public static string Now()
            {
                return "/now";
            }
        }
    }

    /// <summary>
    /// Links to Application's routes, as a client would call them. Resolve from the container, or read it off a template.
    /// </summary>
    public sealed class ApplicationLinks
    {
        private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;
        private global::TestApp.ApplicationLinks.ClockLinks? _Clock;

        public ApplicationLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
        {
            _context = context;
        }

        public global::TestApp.ApplicationLinks.ClockLinks Clock => _Clock ??= new ClockLinks(_context);

        public sealed class ClockLinks
        {
            private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;

            public ClockLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
            {
                _context = context;
            }

            public string Now()
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.Clock.Now());
            }

            public string NowAbsolute()
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.Clock.Now());
            }
        }
    }
}
