using System.Reflection;
using System.Text.RegularExpressions;
using Hardened.Idl.BuildTask;
using Microsoft.Build.Framework;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// Every input <see cref="ExtractSpecTask"/> declares, against what the targets files actually pass.
/// </summary>
/// <remarks>
/// <para>
/// A unit test over the task cannot catch this. The task had read <c>ResponseModel</c> correctly
/// since the property existed, and every test of it passed - the Smithy targets file simply never
/// wrote the attribute, so a project setting <c>$(HardenedResponseModel)</c> got the default and got
/// it silently. The generated interfaces were valid C#, just for a mode nobody asked for, which is
/// why nothing downstream noticed either.
/// </para>
/// <para>
/// So the assertion is about the wiring rather than about any one property: a task input that no
/// targets file passes is dead, and the next one added will be dead the same way unless something
/// compares the two lists. Both front ends are checked here rather than one test per repository
/// folder, because the shell is shared and a property added to it has to reach both.
/// </para>
/// <para>
/// The exclusions are outputs and per-front-end inputs, named individually rather than filtered by
/// a rule - a rule would quietly absorb the next genuinely missing one.
/// </para>
/// </remarks>
public class TargetsWiringTests {

    public static TheoryData<string, string> Targets() => new() {
        { "Hardened.Smithy.SourceGenerator", "ExtractSmithySpec" },
        { "Hardened.OpenApi.SourceGenerator", "ExtractOpenApiSpec" }
    };

    [Theory]
    [MemberData(nameof(Targets))]
    public void EveryTaskInput_IsPassedByTheTargetsFile(string generator, string taskName) {
        var targets = ReadTargets(generator);
        var invocation = Invocation(targets, taskName);

        var missing = Inputs()
            .Where(name => !Regex.IsMatch(invocation, $@"\b{name}\s*="))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{generator}.targets invokes {taskName} without passing: {string.Join(", ", missing)}. " +
            "The task reads these and would silently use its default.");
    }

    /// <summary>
    /// Settable inputs the shared shell itself declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>DeclaredOnly</c> because Microsoft.Build.Utilities.Task contributes BuildEngine and
    /// HostObject, which MSBuild sets on the instance and no targets file ever writes. Inheriting
    /// them into the comparison makes every front end fail for something that is not its business.
    /// </para>
    /// <para>
    /// It also scopes the check to the shell rather than to each subclass. A front end's own input -
    /// ServiceShapeId on the Smithy task - is declared where it is used and passed in the one file
    /// that knows about it, so it carries no parity risk. The shell is the half that has two callers
    /// and can lose one of them.
    /// </para>
    /// <para>
    /// <see cref="OutputAttribute"/> marks the other direction, and a property with no setter is not
    /// something a targets file can supply.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> Inputs() =>
        typeof(ExtractSpecTask)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.CanWrite)
            .Where(property => property.GetCustomAttribute<OutputAttribute>() == null)
            .Select(property => property.Name);

    /// <summary>The text of one task invocation, from its opening tag to the end of its attributes.</summary>
    private static string Invocation(string targets, string taskName) {
        var match = Regex.Match(targets, $@"<{taskName}\b[^>]*>", RegexOptions.Singleline);

        Assert.True(match.Success, $"No <{taskName}> invocation found in the targets file.");

        return match.Value;
    }

    private static string ReadTargets(string generator) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Targets", $"{generator}.targets"));
}
