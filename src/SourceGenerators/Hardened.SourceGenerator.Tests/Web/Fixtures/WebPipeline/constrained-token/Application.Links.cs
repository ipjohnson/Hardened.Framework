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

        public static class Item
        {

            public static string Get(global::System.Int32 id)
            {
                return "/items/" + global::System.Convert.ToString(id, global::System.Globalization.CultureInfo.InvariantCulture);
            }

            public static string BySlug(global::System.String name)
            {
                return "/slugs/" + global::System.Uri.EscapeDataString(name);
            }
        }
    }

    /// <summary>
    /// Links to Application's routes, as a client would call them. Resolve from the container, or read it off a template.
    /// </summary>
    public sealed class ApplicationLinks
    {
        private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;
        private global::TestApp.ApplicationLinks.ItemLinks? _Item;

        public ApplicationLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
        {
            _context = context;
        }

        public global::TestApp.ApplicationLinks.ItemLinks Item => _Item ??= new ItemLinks(_context);

        public sealed class ItemLinks
        {
            private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;

            public ItemLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
            {
                _context = context;
            }

            public string Get(global::System.Int32 id)
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.Item.Get(id));
            }

            public string GetAbsolute(global::System.Int32 id)
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.Item.Get(id));
            }

            public string BySlug(global::System.String name)
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.Item.BySlug(name));
            }

            public string BySlugAbsolute(global::System.String name)
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.Item.BySlug(name));
            }
        }
    }
}
