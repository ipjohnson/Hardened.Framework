using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;
using Hardened.Web.Runtime.CacheControl;

namespace Hardened.Web.Runtime.Configuration
{
    public interface IStaticContentConfiguration
    {
        string Path { get; }

        CacheControlType CacheControlType { get; }

        int CacheMaxAge { get; }

        bool EnableETag { get; }

        string? FallBackFile { get; }

        Action<IExecutionContext>? OnPrepareResponse { get; }
    }

    public class StaticContentConfiguration : IStaticContentConfiguration
    {
        public string Path { get; set; } = "wwwroot";

        public CacheControlType CacheControlType { get; set; } = CacheControlType.MaxAge | CacheControlType.Public;

        public int CacheMaxAge { get; set; } = 0;

        public bool EnableETag { get; set; } = true;

        public string? FallBackFile { get; set; }

        public bool CompressTextContent { get; set; } = true;

        public Action<IExecutionContext>? OnPrepareResponse { get; set; }
    }
}
