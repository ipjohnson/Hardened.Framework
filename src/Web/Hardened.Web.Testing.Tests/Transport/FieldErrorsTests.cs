using Xunit;

namespace Hardened.Web.Testing.Tests.Transport;

public class FieldErrorsTests
{
    /// <summary>A model whose errors throw when read.</summary>
    public sealed class ThrowingModel
    {
        public IEnumerable<object> Errors => throw new InvalidOperationException("unreadable");
    }

    /// <summary>A model whose errors are text rather than a list.</summary>
    public sealed record TextModel(string Errors);

    /// <summary>A model whose errors are neither text nor a list.</summary>
    public sealed record NumberModel(int Errors);

    /// <summary>A field error the way a generated model might carry it, with either half missing.</summary>
    public sealed record PartialModel(string? Field, string? Code);

    public sealed record PartialValidationModel(IReadOnlyList<object?> Errors);

    private static string? Describe(object? body, string? content = null) =>
        FieldErrors.Describe(
            new ClientAnswer(400, body, new Dictionary<string, string>(), Content: content)
        );

    [Theory]
    [InlineData("""{"errors":[{"field":"id","code":"range"}]}""", "Its errors: id (range).")]
    [InlineData("""{"Errors":[{"Field":"id","Code":"range"}]}""", "Its errors: id (range).")]
    [InlineData("""{"errors":[{"field":"id"}]}""", "Its errors: id.")]
    [InlineData("""{"errors":[{"field":"id","code":null}]}""", "Its errors: id.")]
    [InlineData("""{"errors":[{"code":"required"}]}""", "Its errors: required.")]
    [InlineData("""{"errors":[{"field":"count","code":7}]}""", "Its errors: count (7).")]
    [InlineData(
        """{"errors":[{"field":"a","code":"x"},"text",{"other":1}]}""",
        "Its errors: a (x)."
    )]
    public void TextNamesEachFieldError(string content, string expected)
    {
        Assert.Equal(expected, Describe(null, content));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"type":"ValidationError"}""")]
    [InlineData("""{"errors":"bad"}""")]
    [InlineData("""{"errors":[]}""")]
    [InlineData("""{"errors":[{"other":1}]}""")]
    public void TextWithNoFieldErrorsDescribesNothing(string content)
    {
        Assert.Null(Describe(null, content));
    }

    /// <summary>Text that describes nothing is still read, so the model is not consulted.</summary>
    [Fact]
    public void TextIsReadBeforeTheModel()
    {
        var model = new ValidationModel([new FieldModel("quantity", "range")]);

        Assert.Null(Describe(model, """{"errors":[]}"""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoTextReadsTheModel(string? content)
    {
        var model = new ValidationModel([new FieldModel("quantity", "range")]);

        Assert.Equal("Its errors: quantity (range).", Describe(model, content));
    }

    [Fact]
    public void AModelErrorMissingEitherHalfIsNamedByTheOther()
    {
        var model = new PartialValidationModel([
            new PartialModel("id", null),
            null,
            new PartialModel(null, "required"),
            new PartialModel(null, null),
            "no field or code",
        ]);

        Assert.Equal("Its errors: id, required.", Describe(model));
    }

    [Fact]
    public void AModelWithNoErrorsDescribesNothing()
    {
        object?[] bodies =
        [
            null,
            "text",
            new Reply(400, null, new Dictionary<string, string>()),
            new TextModel("errors"),
            new NumberModel(3),
        ];

        Assert.All(bodies, body => Assert.Null(Describe(body)));
    }

    /// <summary>A failure message is being written, so a model that throws adds nothing to it.</summary>
    [Fact]
    public void AModelThatThrowsDescribesNothing()
    {
        Assert.Null(Describe(new ThrowingModel()));
    }
}
