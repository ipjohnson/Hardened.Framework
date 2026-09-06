using System.Collections.Generic;
using Hardened.Generation.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// What <c>$(HardenedBindCancellationToken)</c> puts on a generated interface.
/// </summary>
/// <remarks>
/// <para>
/// A described handler implements a signature it did not write, so a parameter the description does
/// not name can only reach it this way. The flag exists rather than the parameter being
/// unconditional because adding one stops every existing implementation compiling, which is a break
/// worth taking deliberately and once.
/// </para>
/// <para>
/// The other half of the pair is <c>SpecHandlerModelBuilder</c>, which binds the argument the
/// dispatch passes. The two are written by different halves of the build and read the same flag off
/// the model, which is what keeps the call and the signature agreeing.
/// </para>
/// </remarks>
public class BindCancellationTokenTests {

    private static ServiceModel Service() =>
        new() {
            Tag = "Job",
            Operations = new List<OperationModel> {
                new() {
                    OperationId = "getJob",
                    Path = "/jobs/{jobId}",
                    HttpMethod = "GET",
                    Tag = "Job",
                    SuccessStatusCode = 200,
                    ResponseRef = "#/components/schemas/Job",
                    Parameters = new List<ParameterModel> {
                        new() { Name = "jobId", In = "path", IsRequired = true, Type = "integer", Format = "int32" }
                    }
                },
                new() {
                    OperationId = "createJob",
                    Path = "/jobs",
                    HttpMethod = "POST",
                    Tag = "Job",
                    SuccessStatusCode = 201,
                    RequestBodyRef = "#/components/schemas/NewJob",
                    ResponseRef = "#/components/schemas/Job"
                }
            }
        };

    [Fact]
    public void OffByDefault() {
        var result = EmitterHarness.ServiceInterface(Service());

        Assert.Contains("Task<Job> GetJob(int jobId);", result);
        Assert.DoesNotContain("CancellationToken", result);
    }

    [Fact]
    public void OnPutsTheTokenLastOnEveryMethod() {
        var result = EmitterHarness.ServiceInterface(Service(), bindCancellationToken: true);

        // The short name, under a using the emitter registered. Generated code a person opens reads
        // the way they would have written it.
        Assert.Contains("using System.Threading;", result);
        Assert.Contains(
            "Task<Job> GetJob(int jobId, CancellationToken cancellationToken);",
            result);
    }

    /// <summary>
    /// After the body, which is where a C# author writes one. A description names its parameters
    /// and its body separately, so nothing else in the list is interleaved either.
    /// </summary>
    [Fact]
    public void TheTokenGoesAfterTheBody() {
        var result = EmitterHarness.ServiceInterface(Service(), bindCancellationToken: true);

        Assert.Contains(
            "Task<Job> CreateJob(NewJob body, CancellationToken cancellationToken);",
            result);
    }

    /// <summary>
    /// An operation taking nothing still takes the token, because the flag is a whole-spec answer.
    /// One interface with some methods bound and some not is worse to read and worse to migrate.
    /// </summary>
    [Fact]
    public void AnOperationWithNoParametersTakesItToo() {
        var service = new ServiceModel {
            Tag = "Job",
            Operations = new List<OperationModel> {
                new() {
                    OperationId = "listJobs",
                    Path = "/jobs",
                    HttpMethod = "GET",
                    Tag = "Job",
                    SuccessStatusCode = 200,
                    ResponseRef = "#/components/schemas/JobList"
                }
            }
        };

        var result = EmitterHarness.ServiceInterface(service, bindCancellationToken: true);

        Assert.Contains(
            "Task<JobList> ListJobs(CancellationToken cancellationToken);",
            result);
    }

    /// <summary>
    /// A streamed operation takes it too, after the parameters and before nothing else. Its return
    /// type is an <c>IAsyncEnumerable</c> rather than a <c>Task</c>, which is the shape where a C#
    /// author writes <c>[EnumeratorCancellation]</c> on the implementation's own parameter.
    /// </summary>
    [Fact]
    public void AStreamedOperationTakesItAfterItsParameters() {
        var service = new ServiceModel {
            Tag = "Job",
            Operations = new List<OperationModel> {
                new() {
                    OperationId = "jobEvents",
                    Path = "/jobs/{jobId}/events",
                    HttpMethod = "GET",
                    Tag = "Job",
                    SuccessStatusCode = 200,
                    ItemSchemaRef = "#/components/schemas/JobEvent",
                    Parameters = new List<ParameterModel> {
                        new() { Name = "jobId", In = "path", IsRequired = true, Type = "integer", Format = "int32" }
                    }
                }
            }
        };

        var result = EmitterHarness.ServiceInterface(service, bindCancellationToken: true);

        Assert.Contains(
            "IAsyncEnumerable<JobEvent> JobEvents(int jobId, CancellationToken cancellationToken);",
            result);
    }
}
