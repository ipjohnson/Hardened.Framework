using Hardened.Requests.Abstract.Links;
using TestApp;

namespace TestApp
{
    /// <summary>
    /// The routes Application declares, as paths. For a link a client can call, use ApplicationLinks.
    /// </summary>
    public static class ApplicationRoutes
    {

        public static class SignUp
        {

            public static string SignUpPost()
            {
                return "/sign-up";
            }
        }
    }

    /// <summary>
    /// Links to Application's routes, as a client would call them. Resolve from the container, or read it off a template.
    /// </summary>
    public sealed class ApplicationLinks
    {
        private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;
        private global::TestApp.ApplicationLinks.SignUpLinks? _SignUp;

        public ApplicationLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
        {
            _context = context;
        }

        public global::TestApp.ApplicationLinks.SignUpLinks SignUp => _SignUp ??= new SignUpLinks(_context);

        public sealed class SignUpLinks
        {
            private readonly global::Hardened.Requests.Abstract.Links.ILinkContext _context;

            public SignUpLinks(global::Hardened.Requests.Abstract.Links.ILinkContext context)
            {
                _context = context;
            }

            public string SignUpPost()
            {
                return _context.Resolve(global::TestApp.ApplicationRoutes.SignUp.SignUpPost());
            }

            public string SignUpPostAbsolute()
            {
                return _context.Absolute(global::TestApp.ApplicationRoutes.SignUp.SignUpPost());
            }
        }
    }
}
