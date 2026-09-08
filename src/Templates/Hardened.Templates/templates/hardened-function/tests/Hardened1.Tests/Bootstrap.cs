#if (nsubstitute)
using DependencyModules.NSubstitute;
#endif
#if (moq)
using DependencyModules.Moq;
#endif
#if (fakeiteasy)
using DependencyModules.FakeItEasy;
#endif
using Hardened.Functions.Testing;
#if (aws)
using Hardened.Aws.Lambda.Testing;
#endif
#if (gcp)
using Hardened.Gcp.CloudRun.Testing;
using Hardened.Web.Testing;
#endif
using Hardened.Shared.Testing.Attributes;
using Hardened1;

// The application under test. The real module graph is applied and startup services run, so there
// is no separate test wiring to keep in step.
[assembly: HardenedTestEntryPoint(typeof(Application))]

// Makes the generated test façades resolvable, so a test can take Application.Queues and send
// through it.
[assembly: FunctionTesting]

#if (aws)
// Raises the fidelity. Without it a message goes straight into the pipeline, which covers routing,
// binding and the handler but names no cloud. With it the message is packed into the envelope AWS
// actually sends and goes in through the invocation loop, so the adapter and the payload peek are
// exercised too. A test method reads the same either way - delete this line to drop back down.
[assembly: LambdaTesting]
#endif
#if (gcp)
// Raises the fidelity. Without it a message goes straight into the pipeline, which covers routing,
// binding and the handler but names no cloud. With it the message is built as the request Cloud
// Run actually receives - a Pub/Sub push, a CloudEvent, a Scheduler job's POST - and posted to the
// test's host, so the front door and the adapter are exercised too. A test method reads the same
// either way - delete these two lines to drop back down. [WebTesting] is the host the request is
// posted to, and [CloudRunTesting] says so if it is missing.
[assembly: CloudRunTesting]
[assembly: WebTesting]
#endif

// The mock library. [Mock] on a parameter asks this attribute for the double and builds nothing
// itself, so without it a [Mock] parameter fails with "Mock library not found". The package that
// carries it is in Hardened1.Tests.csproj.
#if (nsubstitute)
[assembly: NSubstituteSupport]
#endif
#if (moq)
[assembly: MoqSupport]
#endif
#if (fakeiteasy)
[assembly: FakeItEasySupport]
#endif
