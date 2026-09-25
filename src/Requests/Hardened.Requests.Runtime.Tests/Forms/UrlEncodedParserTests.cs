using Hardened.Requests.Abstract.Forms;
using Hardened.Requests.Runtime.Forms;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Forms;

public class UrlEncodedParserTests
{
    private static string Fields(int count, Func<int, string> name) =>
        string.Join("&", Enumerable.Range(0, count).Select(i => name(i) + "=" + i));

    [Fact]
    public void FieldsAreDecoded()
    {
        var form = UrlEncodedParser.Parse("name=Ada+Lovelace&email=ada%40example.com&sum=1%2B1");

        Assert.Equal("Ada Lovelace", form.Get("name").ToString());
        Assert.Equal("ada@example.com", form.Get("email").ToString());
        Assert.Equal("1+1", form.Get("sum").ToString());
    }

    [Fact]
    public void AFieldWithoutAnEqualsSignHasAnEmptyValue()
    {
        var form = UrlEncodedParser.Parse("flag&a=1");

        Assert.Equal("", form.Get("flag").ToString());
        Assert.Equal("1", form.Get("a").ToString());
    }

    [Fact]
    public void EmptyPairsAreSkipped()
    {
        var form = UrlEncodedParser.Parse("&&a=1&&b=2&");

        Assert.Equal(2, form.Count);
        Assert.Equal("1", form.Get("a").ToString());
        Assert.Equal("2", form.Get("b").ToString());
    }

    [Fact]
    public void ABodyOfSeparatorsIsAnEmptyForm()
    {
        Assert.Same(EmptyFormCollection.Instance, UrlEncodedParser.Parse("&&&"));
    }

    [Fact]
    public void RepeatedNamesAccumulate()
    {
        var form = UrlEncodedParser.Parse("tag=a&tag=b&tag=c");

        Assert.Equal(new[] { "a", "b", "c" }, form.Get("tag").ToArray());
    }

    [Fact]
    public void TheCapIsRead()
    {
        var form = UrlEncodedParser.Parse(Fields(UrlEncodedParser.MaxFields, i => "f" + i));

        Assert.Equal(UrlEncodedParser.MaxFields, form.Count);
    }

    [Fact]
    public void OneFieldPastTheCapIsRefused()
    {
        var exception = Assert.Throws<FormatException>(() =>
            UrlEncodedParser.Parse(Fields(UrlEncodedParser.MaxFields + 1, i => "f" + i))
        );

        Assert.Equal("The body has more than 1024 fields.", exception.Message);
    }

    /// <summary>
    /// Each repeat counts, because the cost is in the values: every repeat of a name grows one
    /// <c>StringValues</c> by a copy.
    /// </summary>
    [Fact]
    public void RepeatsOfOneNameCountTowardTheCap()
    {
        Assert.Throws<FormatException>(() =>
            UrlEncodedParser.Parse(Fields(UrlEncodedParser.MaxFields + 1, _ => "same"))
        );
    }

    [Fact]
    public void EmptyPairsDoNotCountTowardTheCap()
    {
        var body = string.Join(
            "&&",
            Enumerable.Range(0, UrlEncodedParser.MaxFields).Select(i => "f" + i + "=1")
        );

        Assert.Equal(UrlEncodedParser.MaxFields, UrlEncodedParser.Parse(body).Count);
    }
}
