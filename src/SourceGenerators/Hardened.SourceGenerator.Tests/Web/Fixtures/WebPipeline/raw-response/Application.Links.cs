using Hardened.Requests.Abstract.Links;
using TestApp;

namespace TestApp
{
    /// <summary>
    /// The routes Application declares, as paths. For a link a client can call, use ApplicationLinks.
    /// </summary>
    public static class ApplicationRoutes
    {

        public static class Blob
        {

            public static string BlobGet()
            {
                return "/blob";
            }
        }
    }

    /// <summary>
    /// Links to Application's routes, as a client would call them. Resolve from the container, or read it off a template.
    /// </summary>
    public sealed class ApplicationLinks
    {
        private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;
        private global::TestApp.ApplicationLinks.BlobLinks? _Blob;

        public ApplicationLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
        {
            _context = context;
        }

        public global::TestApp.ApplicationLinks.BlobLinks Blob => _Blob ??= new BlobLinks(_context);

        public sealed class BlobLinks
        {
            private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;

            public BlobLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
            {
                _context = context;
            }

            public string BlobGet()
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.Blob.BlobGet());
            }

            public string BlobGetAbsolute()
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.Blob.BlobGet());
            }
        }
    }
}
