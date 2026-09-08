#if (gcp)
using Hardened.Gcp.CloudRun.Runtime;
#endif
#if (azure && topic)
using Hardened.Azure.Functions.ServiceBus;
#endif
#if (azure && change)
using Hardened.Azure.Functions.CosmosDb;
#endif
using Hardened.Shared.Runtime.Attributes;

namespace Hardened1;

#if (aws)
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
#endif
#if (gcp)
/// <summary>
/// The application. The host is the one thing it names.
/// </summary>
/// <remarks>
/// <b>There is no adapter module attribute here, and that is the point.</b> The envelope the front
/// door recognises and the filters it needs arrive because the handler carries a trigger
/// attribute: the generator reads the build property the adapter package declares and registers
/// the module for you. What this file does name is the host, the way a web application names
/// [KestrelRuntime], because a Cloud Run service is a container listening on a port and that is a
/// fact about the deployment. Moving to another cloud changes this attribute and a package
/// reference.
///
/// partial is not optional - the generator writes the other half.
/// </remarks>
[HardenedModule]
[CloudRunRuntime]
public partial class Application;
#endif
#if (azure)
/// <summary>
/// The application. What it runs on is not written here.
/// </summary>
/// <remarks>
/// <b>There is no host module attribute, and that is the point.</b> The adapter and the filters
/// it needs arrive because the handler carries a trigger attribute: the generator reads the build
/// property the adapter package declares, registers the module for you, and writes the function
/// the Functions host indexes. Nothing in this file names a cloud, so moving to another one is a
/// package reference.
///
/// partial is not optional - the generator writes the other half.
/// </remarks>
[HardenedModule]
#if (topic)
// The one deployment fact the neutral trigger has no slot for. [Topic("orders")] names the topic,
// and a Service Bus topic is read through a subscription; this names the one this function app
// reads through, and the generator writes it into the function's binding. Without it the build
// fails with HRDAZ003 rather than deploying a function nothing delivers to.
[ServiceBusModule(Subscription = "Hardened1")]
#endif
#if (change)
// The one deployment fact the neutral trigger has no slot for. [Change("orders")] names the
// container, and a Cosmos DB container lives in a database; this names it, and the generator
// writes it into the function's binding. Without it the build fails with HRDAZ003 rather than
// deploying a function nothing delivers to.
[CosmosDbModule(Database = "Hardened1")]
#endif
public partial class Application;
#endif
