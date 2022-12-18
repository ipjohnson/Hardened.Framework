using Hardened.Requests.Abstract.Execution;
using Hardened.Web.Runtime.CacheControl;

namespace Hardened.Web.Runtime.Configuration;

public interface IStaticContentConfiguration
{
    string Path { get; }

    CacheControlEnum CacheControlType { get; }

    int? CacheMaxAge { get; }
    
    bool Immutable { get; }

    bool EnableETag { get; }

    string? FallBackFile { get; }

    bool CompressTextContent { get; }

    Action<IExecutionContext>? OnPrepareResponse { get; }
}

public class StaticContentConfiguration : IStaticContentConfiguration
{
    public string Path { get; set; } = "wwwroot";

    public CacheControlEnum CacheControlType { get; set; } = CacheControlEnum.MaxAge | CacheControlEnum.Public;

    public int? CacheMaxAge { get; set; } = 0;
    
    public bool Immutable { get; set; }

    public bool EnableETag { get; set; } = true;

    public string? FallBackFile { get; set; }

    public bool CompressTextContent { get; set; } = true;

    public Action<IExecutionContext>? OnPrepareResponse { get; set; }
}