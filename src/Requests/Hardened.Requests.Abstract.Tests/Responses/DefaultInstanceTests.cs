using System.Reflection;
using Hardened.Requests.Abstract.Responses;

namespace Hardened.Requests.Abstract.Tests.Responses;

/// <summary>
/// The shared instance each bare error record offers for a handler with nothing to add.
/// </summary>
/// <remarks>
/// <para>
/// <c>return NotFound.Default;</c> is the whole of a 404 that has nothing more to say, and it
/// allocates nothing: the instance is a static, and a response set holds it by reference. The
/// sweep finds every record that has one rather than naming them, so a record added later is held
/// to the same shape.
/// </para>
/// <para>
/// Which records have one is a rule rather than a list: every problem-kind record whose
/// constructor needs nothing a caller would have to supply. <c>RateLimited</c> needs a delay and
/// <c>MethodNotAllowed</c> an <c>Allow</c>; <c>NotAcceptable</c> carries no message.
/// </para>
/// </remarks>
public class DefaultInstanceTests {

    public static TheoryData<Type> RecordsWithADefault {
        get {
            var data = new TheoryData<Type>();

            foreach (var type in Records()) {
                data.Add(type);
            }

            return data;
        }
    }

    /// <summary>Every response record with a static <c>Default</c>, found rather than named.</summary>
    private static IEnumerable<Type> Records() {
        foreach (var type in typeof(NotFound).Assembly.GetExportedTypes()) {
            if (type.IsGenericTypeDefinition || type.GetCustomAttribute<HttpStatusAttribute>() == null) {
                continue;
            }

            if (type.GetField("Default", BindingFlags.Public | BindingFlags.Static) != null) {
                yield return type;
            }
        }
    }

    [Theory]
    [MemberData(nameof(RecordsWithADefault))]
    public void ADefault_IsAnInstanceOfItsOwnTypeAtItsOwnStatus(Type type) {
        var field = type.GetField("Default", BindingFlags.Public | BindingFlags.Static)!;
        var instance = field.GetValue(null);

        Assert.True(field.IsInitOnly);
        Assert.IsType(type, instance);
        Assert.Equal(
            type.GetCustomAttribute<HttpStatusAttribute>()!.StatusCode,
            ((IHttpStatusResponse)instance!).Status);
    }

    /// <summary>The message is generic, and there is one: the point is a body that says something.</summary>
    [Theory]
    [MemberData(nameof(RecordsWithADefault))]
    public void ADefault_CarriesAGenericDetail(Type type) {
        var instance = type.GetField("Default", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        var detail = (string?)type.GetProperty("Detail")!.GetValue(instance);

        Assert.False(string.IsNullOrWhiteSpace(detail));
        Assert.EndsWith(".", detail);
    }

    /// <summary>
    /// Every problem-kind record whose constructor a caller need not feed has one, and the three
    /// that cannot have one do not.
    /// </summary>
    [Fact]
    public void EveryRecordThatCanHaveADefaultDoes() {
        var withDefault = new HashSet<Type>(Records());

        Assert.Equal(18, withDefault.Count);
        Assert.Contains(typeof(NotFound), withDefault);
        Assert.Contains(typeof(Unauthorized), withDefault);
        Assert.Contains(typeof(ServiceUnavailable), withDefault);
        Assert.DoesNotContain(typeof(RateLimited), withDefault);
        Assert.DoesNotContain(typeof(MethodNotAllowed), withDefault);
        Assert.DoesNotContain(typeof(NotAcceptable), withDefault);
    }

    [Fact]
    public void NotFound_DefaultNamesNoParticularResource() {
        Assert.Equal("resource", NotFound.Default.Resource);
        Assert.Equal(404, NotFound.Default.Status);
    }

    /// <summary>Returning the same instance twice is the same answer; nothing is built per return.</summary>
    [Fact]
    public void ADefault_IsOneInstance() {
        Assert.Same(NotFound.Default, NotFound.Default);
        Assert.Same(Conflict.Default, Conflict.Default);
    }
}
