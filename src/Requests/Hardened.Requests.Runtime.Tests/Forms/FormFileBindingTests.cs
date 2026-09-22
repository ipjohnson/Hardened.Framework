using Hardened.Requests.Abstract.Forms;
using Hardened.Requests.Runtime.Forms;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;
using ValidationException = Hardened.Requests.Runtime.Validation.ValidationException;

namespace Hardened.Requests.Runtime.Tests.Forms;

public class FormFileBindingTests
{
    [Fact]
    public void ARequiredFileThatWasSentIsReturned()
    {
        var file = Substitute.For<IFormFile>();

        Assert.Same(file, FormFileBinding.Required(file, "file"));
    }

    [Fact]
    public void AMissingRequiredFileIsRequiredNamingTheField()
    {
        var exception = Assert.Throws<ValidationException>(() =>
            FormFileBinding.Required(null, "upload")
        );

        var error = Assert.Single(exception.ValidationResult.Errors);

        Assert.Equal("upload", error.Field);
        Assert.Equal("required", error.Code);
        Assert.Equal("upload is required.", error.Message);
    }

    [Fact]
    public void RequiredFilesAreReturnedWhenAnyWereSent()
    {
        var files = new[] { Substitute.For<IFormFile>() };

        Assert.Same(files, FormFileBinding.RequiredMany(files, "photos"));
    }

    [Fact]
    public void NoFilesWhereSomeAreRequiredIsRequired()
    {
        var exception = Assert.Throws<ValidationException>(() =>
            FormFileBinding.RequiredMany(Array.Empty<IFormFile>(), "photos")
        );

        Assert.Equal("photos", Assert.Single(exception.ValidationResult.Errors).Field);
    }

    [Fact]
    public void OptionalFilesAreNullWhenNoneWereSent()
    {
        Assert.Null(FormFileBinding.OptionalMany(Array.Empty<IFormFile>()));

        var files = new[] { Substitute.For<IFormFile>() };

        Assert.Same(files, FormFileBinding.OptionalMany(files));
    }

    /// <summary>
    /// A url-encoded form lists its field names, and has no files - which is the interface's own
    /// answer, for an implementation written before files existed.
    /// </summary>
    [Fact]
    public void AUrlEncodedFormListsItsFieldsAndHasNoFiles()
    {
        IFormCollection form = new SimpleFormCollection(
            new Dictionary<string, StringValues> { ["a"] = "1", ["b"] = "2" }
        );

        Assert.Equal(["a", "b"], form.Keys);
        Assert.Null(form.GetFile("a"));
        Assert.Empty(form.GetFiles("a"));
    }

    [Fact]
    public void AnEmptyFormHasNoKeysAndNoFiles()
    {
        IFormCollection form = EmptyFormCollection.Instance;

        Assert.Empty(form.Keys);
        Assert.Null(form.GetFile("a"));
        Assert.Empty(form.GetFiles("a"));
    }
}
