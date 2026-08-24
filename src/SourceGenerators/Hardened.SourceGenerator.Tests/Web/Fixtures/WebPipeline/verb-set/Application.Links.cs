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

        public static class Ticket
        {

            public static string Remove(global::System.String id)
            {
                return "/tickets/" + global::System.Uri.EscapeDataString(id);
            }

            public static string Get(global::System.String id)
            {
                return "/tickets/" + global::System.Uri.EscapeDataString(id);
            }

            public static string Replace(global::System.String id)
            {
                return "/tickets/" + global::System.Uri.EscapeDataString(id);
            }
        }
    }

    /// <summary>
    /// Links to Application's routes, as a client would call them. Resolve from the container, or read it off a template.
    /// </summary>
    public sealed class ApplicationLinks
    {
        private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;
        private global::TestApp.ApplicationLinks.TicketLinks? _Ticket;

        public ApplicationLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
        {
            _context = context;
        }

        public global::TestApp.ApplicationLinks.TicketLinks Ticket => _Ticket ??= new TicketLinks(_context);

        public sealed class TicketLinks
        {
            private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;

            public TicketLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
            {
                _context = context;
            }

            public string Remove(global::System.String id)
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.Ticket.Remove(id));
            }

            public string RemoveAbsolute(global::System.String id)
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.Ticket.Remove(id));
            }

            public string Get(global::System.String id)
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.Ticket.Get(id));
            }

            public string GetAbsolute(global::System.String id)
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.Ticket.Get(id));
            }

            public string Replace(global::System.String id)
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.Ticket.Replace(id));
            }

            public string ReplaceAbsolute(global::System.String id)
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.Ticket.Replace(id));
            }
        }
    }
}
