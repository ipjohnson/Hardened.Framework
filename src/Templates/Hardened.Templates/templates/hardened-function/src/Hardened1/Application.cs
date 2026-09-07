using Hardened.Shared.Runtime.Attributes;

namespace Hardened1;

/// <summary>
/// The application. What it runs on is not written here.
/// </summary>
/// <remarks>
/// <b>There is no host module attribute, and that is the point.</b> The adapter, its serializer
/// and the filters it needs all arrive because the handler carries a trigger attribute: the
/// generator reads the build property the host package declares and registers the module for you.
/// Nothing in this file names a cloud, so moving to another one is a package reference.
///
/// partial is not optional - the generator writes the other half.
/// </remarks>
[HardenedModule]
public partial class Application;
