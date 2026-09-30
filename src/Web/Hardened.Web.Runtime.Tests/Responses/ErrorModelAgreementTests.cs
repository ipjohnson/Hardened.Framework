using System.Reflection;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Responses;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Responses;

/// <summary>
/// A refusal the framework raises and a problem record a handler returns, at the same status, send
/// the same <c>type</c> and <c>title</c>.
/// </summary>
/// <remarks>
/// <c>ErrorModel</c> is in <c>Hardened.Requests.Abstract</c>, which cannot reference the records, so
/// it derives its values from the status. This holds the derivation to every record's own answer.
/// </remarks>
public class ErrorModelAgreementTests
{
    public static TheoryData<Type> ProblemRecords()
    {
        var data = new TheoryData<Type>();

        foreach (
            var type in typeof(NotFound)
                .Assembly.GetTypes()
                .Where(candidate =>
                    candidate.IsPublic
                    && !candidate.IsGenericTypeDefinition
                    && typeof(IProblemDetails).IsAssignableFrom(candidate)
                    && candidate.GetField("Default", BindingFlags.Public | BindingFlags.Static)
                        != null
                )
        )
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ProblemRecords))]
    public void TheFrameworksRefusalReadsLikeTheRecord(Type record)
    {
        var instance = (IProblemDetails)
            record.GetField("Default", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

        var model = ErrorModel.For(instance.Status);

        Assert.Equal(instance.Type, model.Type);
        Assert.Equal(instance.Title, model.Title);
        Assert.Equal(instance.Status, model.Status);
    }

    [Fact]
    public void RateLimitedHasNoDefaultAndStillAgrees()
    {
        Assert.Equal(ProblemTypes.RateLimited, ErrorModel.TypeFor(429));
        Assert.Equal("Too Many Requests", ErrorModel.For(429).Title);
    }

    [Fact]
    public void TheRefusalsWithNoRecordHaveTypesOfTheirOwn()
    {
        Assert.Equal(ProblemTypes.MethodNotAllowed, ErrorModel.TypeFor(405));
        Assert.Equal(ProblemTypes.NotAcceptable, ErrorModel.TypeFor(406));
    }

    [Fact]
    public void AStatusWithoutAReasonPhraseIsAboutBlank()
    {
        Assert.Equal("about:blank", ErrorModel.TypeFor(499));
    }
}
