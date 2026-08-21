using Hardened.Shared.Runtime.Attributes;
#if (codeFirst)
using Hardened.Web.Runtime.Attributes;
#if (declaredMode)
using Hardened.Requests.Abstract.Responses;
#endif
#endif
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened1;

/// <summary>
/// The library module: this assembly's handlers, services and URL space.
/// </summary>
/// <remarks>
/// The host imports it with the single generated [TemplateModuleNameLibrary] attribute. partial is not
/// optional - the generator writes the other half.
/// </remarks>
[HardenedModule]
[HardenedWebModule]
#if (codeFirst)
// This assembly's URL space. Every route below it is relative to this.
[BasePath("/todos")]
#if (responseMode)
// How every handler in this module declares its responses. Module-wide rather than per-handler:
// two endpoints declaring the same thing two ways means a reader has to check each one. A module
// that wants both splits in two, which is a real boundary rather than an annotation.
[ResponseModel(ResponseModel.Response)]
#endif
#if (unionMode)
// As above, with the C# 15 union keyword instead of the Response struct. The two are the same
// declared set and the same generated dispatch; the union adds exhaustiveness where you match on
// it. Below C# 15 this is a build error naming LangVersion, not a silent downgrade.
[ResponseModel(ResponseModel.Union)]
#endif
#endif
public partial class TemplateModuleNameLibrary;
