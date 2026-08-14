using DependencyModules.Runtime.Attributes;

namespace Hardened.Templates.RazorBlade;

/// <summary>
/// Registers the RazorBlade engine and the serializer that routes template responses to it.
/// </summary>
/// <remarks>
/// Apply this after the core request module. <c>SerializationLocatorService</c> tests later
/// registrations first, and that ordering is what puts a template response ahead of JSON for a
/// request that would satisfy both.
/// </remarks>
[DependencyModule]
public partial class RazorBladeTemplateLibrary { }
