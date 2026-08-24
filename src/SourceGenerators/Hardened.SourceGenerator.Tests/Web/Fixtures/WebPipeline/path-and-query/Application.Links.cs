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

        public static class Search
        {

            public static string SearchGet(global::System.String category)
            {
                return "/search/" + global::System.Uri.EscapeDataString(category);
            }
        }
    }

    /// <summary>
    /// Links to Application's routes, as a client would call them. Resolve from the container, or read it off a template.
    /// </summary>
    public sealed class ApplicationLinks
    {
        private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;
        private global::TestApp.ApplicationLinks.SearchLinks? _Search;

        public ApplicationLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
        {
            _context = context;
        }

        public global::TestApp.ApplicationLinks.SearchLinks Search => _Search ??= new SearchLinks(_context);

        public sealed class SearchLinks
        {
            private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;

            public SearchLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
            {
                _context = context;
            }

            public string SearchGet(global::System.String category)
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.Search.SearchGet(category));
            }

            public string SearchGetAbsolute(global::System.String category)
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.Search.SearchGet(category));
            }
        }
    }
}
