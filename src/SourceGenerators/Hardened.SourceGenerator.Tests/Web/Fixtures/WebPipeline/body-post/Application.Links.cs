using Hardened.Requests.Abstract.Links;
using TestApp;

namespace TestApp
{
    /// <summary>
    /// The routes Application declares, as paths. For a link a client can call, use ApplicationLinks.
    /// </summary>
    public static class ApplicationRoutes
    {

        public static class Order
        {

            public static string Place()
            {
                return "/orders";
            }
        }
    }

    /// <summary>
    /// Links to Application's routes, as a client would call them. Resolve from the container, or read it off a template.
    /// </summary>
    public sealed class ApplicationLinks
    {
        private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;
        private global::TestApp.ApplicationLinks.OrderLinks? _Order;

        public ApplicationLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
        {
            _context = context;
        }

        public global::TestApp.ApplicationLinks.OrderLinks Order => _Order ??= new OrderLinks(_context);

        public sealed class OrderLinks
        {
            private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;

            public OrderLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
            {
                _context = context;
            }

            public string Place()
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.Order.Place());
            }

            public string PlaceAbsolute()
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.Order.Place());
            }
        }
    }
}
