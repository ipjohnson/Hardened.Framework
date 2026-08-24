using Hardened.Requests.Abstract.Links;
using System;
using TestApp;

namespace TestApp
{
    /// <summary>
    /// The routes Application declares, as paths. For a link a client can call, use ApplicationLinks.
    /// </summary>
    public static class ApplicationRoutes
    {

        public static class Widget
        {

            public static string Create()
            {
                return "/widgets";
            }

            public static string Find(global::System.String id)
            {
                return "/widgets/" + global::System.Uri.EscapeDataString(id);
            }
        }
    }

    /// <summary>
    /// Links to Application's routes, as a client would call them. Resolve from the container, or read it off a template.
    /// </summary>
    public sealed class ApplicationLinks
    {
        private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;
        private global::TestApp.ApplicationLinks.WidgetLinks? _Widget;

        public ApplicationLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
        {
            _context = context;
        }

        public global::TestApp.ApplicationLinks.WidgetLinks Widget => _Widget ??= new WidgetLinks(_context);

        public sealed class WidgetLinks
        {
            private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;

            public WidgetLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
            {
                _context = context;
            }

            public string Create()
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.Widget.Create());
            }

            public string CreateAbsolute()
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.Widget.Create());
            }

            public string Find(global::System.String id)
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.Widget.Find(id));
            }

            public string FindAbsolute(global::System.String id)
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.Widget.Find(id));
            }
        }
    }
}
