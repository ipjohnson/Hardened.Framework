using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// Which handlers bind a form and a body at once.
/// </summary>
/// <remarks>
/// <para>
/// The decision, not the reporting. A <c>SourceProductionContext</c> only exists inside a running
/// generator, so <c>FormAndBodyDiagnostics.Report</c> can only be exercised through one - which
/// <c>Hardened.Web.SourceGenerator.Tests</c> does. What is worth testing here is the rule itself,
/// which has nothing to do with Roslyn.
/// </para>
/// <para>
/// The rule matters because the failure it prevents is quiet: there is one body, <c>[FromForm]</c>
/// reads it as <c>name=value</c> pairs and a body parameter hands the same bytes to a deserializer,
/// and whichever runs second gets a consumed stream. The handler compiles and routes correctly
/// either way.
/// </para>
/// </remarks>
public class FormAndBodyConflictTests {

    private static ITypeDefinition Type(string name) => TypeDefinition.Get("System", name);

    private static RequestParameterInformation Parameter(
        ParameterBindType bindingType, string name) =>
        new(Type("String"), name, true, null, bindingType, name, 0, null);

    private static RequestHandlerModel Handler(params RequestParameterInformation[] parameters) =>
        new(
            new RequestHandlerNameModel("/sign-in", "POST"),
            TypeDefinition.Get("TestApp", "SignInController"),
            "SignIn",
            TypeDefinition.Get("TestApp.Generated", "SignInController_SignIn"),
            parameters,
            new ResponseInformationModel { ReturnType = Type("String") },
            []);

    [Fact]
    public void AFormAndABodyTogetherConflict() {
        var conflict = FormAndBodyDiagnostics.FindConflict(
            Handler(
                Parameter(ParameterBindType.Form, "username"),
                Parameter(ParameterBindType.Body, "credentials")));

        Assert.NotNull(conflict);
        Assert.Equal("username", conflict!.Value.Form.Name);
        Assert.Equal("credentials", conflict.Value.Body.Name);
    }

    /// <summary>Order does not matter - the body may be declared first.</summary>
    [Fact]
    public void TheDeclarationOrderDoesNotMatter() {
        var conflict = FormAndBodyDiagnostics.FindConflict(
            Handler(
                Parameter(ParameterBindType.Body, "credentials"),
                Parameter(ParameterBindType.Form, "username")));

        Assert.NotNull(conflict);
        Assert.Equal("username", conflict!.Value.Form.Name);
        Assert.Equal("credentials", conflict.Value.Body.Name);
    }

    [Theory]
    [InlineData(ParameterBindType.Form)]
    [InlineData(ParameterBindType.Body)]
    public void OneOrTheOtherAloneIsFine(ParameterBindType bindingType) {
        Assert.Null(
            FormAndBodyDiagnostics.FindConflict(Handler(Parameter(bindingType, "only"))));
    }

    /// <summary>
    /// Several form fields are not a conflict with each other.
    /// </summary>
    /// <remarks>
    /// The ordinary shape of a form handler, and the one a naive "more than one parameter reads
    /// the body" rule would reject. They all read one form, which is read once.
    /// </remarks>
    [Fact]
    public void SeveralFormFieldsAreNotAConflict() {
        Assert.Null(
            FormAndBodyDiagnostics.FindConflict(
                Handler(
                    Parameter(ParameterBindType.Form, "username"),
                    Parameter(ParameterBindType.Form, "password"),
                    Parameter(ParameterBindType.Form, "totp"))));
    }

    [Fact]
    public void AHandlerBindingNeitherIsFine() {
        Assert.Null(
            FormAndBodyDiagnostics.FindConflict(
                Handler(
                    Parameter(ParameterBindType.Path, "id"),
                    Parameter(ParameterBindType.QueryString, "filter"))));
    }
}
