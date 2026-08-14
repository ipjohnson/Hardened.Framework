using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.CacheControl;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// <c>[CacheControl]</c> on a handler and on a controller, so the header it declares can be
/// asserted on a real response rather than in the handler's metadata.
/// </summary>
/// <remarks>
/// The enum is spelled unqualified here on purpose. Attribute arguments are copied into generated
/// source that carries none of this file's usings, so this only compiles because the generator
/// resolves them through the semantic model and re-emits them qualified.
/// </remarks>
[BasePath("/cache")]
public class CacheControlController {

    [Get("/default")]
    [CacheControl]
    public string Default() => "default";

    [Get("/long")]
    [CacheControl(MaxAge = 86400)]
    public string Long() => "long";

    [Get("/none")]
    [CacheControl(Type = CacheControlEnum.NoStore)]
    public string None() => "none";

    [Get("/private")]
    [CacheControl(MaxAge = 60, Type = CacheControlEnum.MaxAge | CacheControlEnum.Private)]
    public string Private() => "private";

    [Get("/unset")]
    public string Unset() => "unset";
}

/// <summary>
/// The controller-level form, which the generator copies onto every handler on the class.
/// </summary>
[BasePath("/cache-all")]
[CacheControl(MaxAge = 30)]
public class CachedEverywhereController {

    [Get("/one")]
    public string One() => "one";

    [Get("/two")]
    public string Two() => "two";
}
