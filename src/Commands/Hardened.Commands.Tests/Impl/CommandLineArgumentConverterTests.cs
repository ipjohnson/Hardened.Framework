using Hardened.Commands.Impl;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Commands.Tests.Impl;

/// <summary>
/// Turning the strings the parser collected into the property types a command model declares.
///
/// <para>
/// The generated binder calls <c>Convert&lt;T&gt;</c> once per option with the property's type, so
/// this is where a command line becomes a typed model — and where a bad value has to become a
/// legible failure rather than a default.
/// </para>
/// </summary>
public class CommandLineArgumentConverterTests {

    private static CommandLineArgumentConverter Converter() =>
        new(new JsonSerializerImpl(
            Options.Create<IJsonSerializerConfiguration>(new JsonSerializerConfiguration())));

    /// <summary>
    /// A string option is handed back untouched rather than deserialised. It matters: the generator
    /// types every option as <c>String</c>, so without this short circuit <c>--name Ada</c> would go
    /// through the JSON reader and fail on an unquoted value.
    /// </summary>
    [Fact]
    public void AStringOptionIsPassedThroughUnchanged() {
        Assert.Equal(
            "Ada Lovelace",
            Converter().Convert<string>("name", CommandOptionType.String, "Ada Lovelace", null));
    }

    /// <summary>
    /// A non-string target with the <c>String</c> option type goes through the JSON serialiser.
    /// This is the path every generated numeric option takes, because the generator types them all
    /// as <c>String</c>.
    /// </summary>
    [Theory]
    [InlineData("42", 42)]
    [InlineData("-7", -7)]
    [InlineData("0", 0)]
    public void ANumericPropertyIsReadAsJson(string argument, int expected) {
        Assert.Equal(expected, Converter().Convert("count", CommandOptionType.String, argument, 0));
    }

    [Fact]
    public void ABooleanPropertyIsReadAsJson() {
        Assert.True(Converter().Convert("verbose", CommandOptionType.String, "true", false));
    }

    /// <summary>
    /// The <c>Number</c> option type converts rather than deserialising, so it accepts the forms
    /// <c>Convert.ChangeType</c> accepts.
    /// </summary>
    [Theory]
    [InlineData("42")]
    [InlineData("+42")]
    [InlineData(" 42 ")]
    public void ANumberOptionIsConverted(string argument) {
        Assert.Equal(42, Converter().Convert("count", CommandOptionType.Number, argument, 0));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    public void ABooleanOptionIsConverted(string argument, bool expected) {
        Assert.Equal(expected, Converter().Convert("verbose", CommandOptionType.Boolean, argument, false));
    }

    /// <summary>
    /// A value that is not a number fails loudly. Silently defaulting to zero is the failure mode
    /// worth guarding: <c>--retries oops</c> would run with no retries and report nothing.
    /// </summary>
    [Fact]
    public void ANumberOptionRejectsAValueThatIsNotANumber() {
        Assert.ThrowsAny<Exception>(
            () => Converter().Convert("count", CommandOptionType.Number, "not-a-number", 0));
    }

    [Fact]
    public void ABooleanOptionRejectsAValueThatIsNotABoolean() {
        Assert.ThrowsAny<Exception>(
            () => Converter().Convert("verbose", CommandOptionType.Boolean, "maybe", false));
    }

    /// <summary>
    /// The type conversion failure a generated application actually hits, since the generator types
    /// every option as <c>String</c>: a numeric property given a value that is not JSON.
    /// </summary>
    [Fact]
    public void ANumericPropertyRejectsAValueThatIsNotJson() {
        Assert.ThrowsAny<Exception>(
            () => Converter().Convert("count", CommandOptionType.String, "not-a-number", 0));
    }

    /// <summary>An option that was never given falls back to the default the binder passed.</summary>
    [Fact]
    public void AnAbsentOptionFallsBackToItsDefault() {
        Assert.Equal(7, Converter().Convert("count", CommandOptionType.Number, null, 7));
    }

    /// <summary>
    /// An absent option with no default cannot be bound. The parser is supposed to have reported it
    /// as missing before reaching here, so this is the last line rather than the first.
    /// </summary>
    [Fact]
    public void AnAbsentOptionWithNoDefaultIsRejected() {
        Assert.Throws<Exception>(
            () => Converter().Convert<string>("name", CommandOptionType.String, null, null));
    }

    /// <summary>
    /// A file option pointing at nothing names both the path and the option, because "file not
    /// found" without either is the error a user cannot act on.
    /// </summary>
    [Fact]
    public void AFileOptionRejectsAPathThatDoesNotExist() {
        var missing = Path.Combine(Path.GetTempPath(), $"hardened-console-{Guid.NewGuid():N}.json");

        var message = Assert.Throws<Exception>(
            () => Converter().Convert<string[]>("config", CommandOptionType.File, missing, null)).Message;

        Assert.Contains(missing, message);
        Assert.Contains("config", message);
    }

    /// <summary>
    /// An option type outside the enum is a generator defect rather than a user error, so it says
    /// which value it did not recognise.
    /// </summary>
    [Fact]
    public void AnUnrecognisedOptionTypeIsRejectedByValue() {
        var message = Assert.Throws<Exception>(
            () => Converter().Convert("count", (CommandOptionType)99, "1", 0)).Message;

        Assert.Contains("Unknown option type", message);
    }
}
