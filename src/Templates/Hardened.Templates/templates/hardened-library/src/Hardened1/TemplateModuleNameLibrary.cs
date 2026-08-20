using Hardened.Shared.Runtime.Attributes;

namespace Hardened1;

/// <summary>
/// The module. A host picks up everything in this assembly with the single generated
/// [TemplateModuleNameLibrary] attribute.
/// </summary>
/// <remarks>
/// No runtime attribute here, deliberately - this assembly names no host, so it composes with
/// whichever one the consuming application chose. partial is not optional: the generator writes
/// the other half, including the attribute consumers apply.
///
/// The class body is empty because it stays empty. Services register themselves where they are
/// declared, so there is no list here to fall out of step with the code.
/// </remarks>
[HardenedModule]
public partial class TemplateModuleNameLibrary;
