using System.Text;
using Hardened.Requests.Runtime.Forms;
using Xunit;
using static Hardened.Requests.Runtime.Tests.Forms.MultipartBodies;

namespace Hardened.Requests.Runtime.Tests.Forms;

public class MultipartFormParserTests
{
    private static MultipartFormCollection Parse(byte[] body, string boundary = Boundary) =>
        MultipartFormParser.Parse(new ArraySegment<byte>(body), boundary);

    [Theory]
    [InlineData("multipart/form-data; boundary=rb-7c4f1e0a9d", "rb-7c4f1e0a9d")]
    [InlineData("multipart/form-data; boundary=\"rb-7c4f1e0a9d\"", "rb-7c4f1e0a9d")]
    [InlineData("multipart/form-data;BOUNDARY=abc", "abc")]
    [InlineData("multipart/form-data; charset=utf-8; boundary=\"a\\\"b\"", "a\"b")]
    public void TheBoundaryIsReadQuotedOrBare(string contentType, string expected)
    {
        Assert.Equal(expected, MultipartFormParser.Boundary(contentType));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("multipart/form-data")]
    [InlineData("multipart/form-data; boundary=")]
    [InlineData("multipart/form-data; charset")]
    public void AContentTypeWithNoBoundaryHasNone(string? contentType)
    {
        Assert.Null(MultipartFormParser.Boundary(contentType));
    }

    /// <summary>RFC 2046 allows 70 characters and no more.</summary>
    [Fact]
    public void ABoundaryLongerThanSeventyIsRefused()
    {
        Assert.NotNull(
            MultipartFormParser.Boundary("multipart/form-data; boundary=" + new string('a', 70))
        );
        Assert.Null(
            MultipartFormParser.Boundary("multipart/form-data; boundary=" + new string('a', 71))
        );
    }

    public static TheoryData<byte[], string> Shapes() =>
        new()
        {
            { RequestBench(), Boundary },
            {
                DotNet("5d1f2c34-8e1b-4a9f-9a61-07c3a6f0e2b1"),
                "5d1f2c34-8e1b-4a9f-9a61-07c3a6f0e2b1"
            },
            {
                Curl("------------------------a1b2c3d4e5f60718"),
                "------------------------a1b2c3d4e5f60718"
            },
        };

    /// <summary>
    /// The RequestBench harness, .NET's <c>MultipartFormDataContent</c> and <c>curl -F</c> all read
    /// as the same two fields and one file.
    /// </summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void EverySendersShapeReadsTheSame(byte[] body, string boundary)
    {
        var form = Parse(body, boundary);

        Assert.Equal("qwertyuiopas", form.Get("tenant").ToString());
        Assert.Equal("0123456789abcdef", form.Get("requestId").ToString());

        var file = form.GetFile("file")!;

        Assert.Equal("file", file.Name);
        Assert.Equal("forms.file.txt", file.FileName);
        Assert.Equal("text/plain", file.ContentType);
        Assert.Equal(Csv.Length, file.Length);

        using var stream = file.OpenReadStream();
        using var copy = new MemoryStream();

        stream.CopyTo(copy);

        Assert.Equal(Csv, copy.ToArray());
    }

    [Fact]
    public void FieldsAreKeysAndFilesAreNot()
    {
        var form = Parse(RequestBench());

        Assert.Equal(2, form.Count);
        Assert.Equal(["tenant", "requestId"], form.Keys);
        Assert.True(form.Get("file").Count == 0);
    }

    /// <summary>Every byte of a file arrives, including the ones that are not UTF-8.</summary>
    [Fact]
    public void ABinaryFileKeepsEveryByte()
    {
        var binary = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();

        var file = Parse(RequestBench(binary)).GetFile("file")!;

        using var copy = new MemoryStream();

        file.OpenReadStream().CopyTo(copy);

        Assert.Equal(binary, copy.ToArray());
    }

    [Fact]
    public void APreambleAndAnEpilogueAreIgnored()
    {
        var body = Join("This is the preamble.\r\n", RequestBench(), "This is the epilogue.");

        Assert.Equal("qwertyuiopas", Parse(body).Get("tenant").ToString());
    }

    [Fact]
    public void PaddingAfterABoundaryIsAllowed()
    {
        var body = Join(
            "--" + Boundary + " \t\r\n",
            "Content-Disposition: form-data; name=\"tenant\"\r\n\r\n",
            "acme\r\n",
            "--" + Boundary + "--\r\n"
        );

        Assert.Equal("acme", Parse(body).Get("tenant").ToString());
    }

    /// <summary>RFC 7578 section 4.4: a part that states no type is <c>text/plain</c>.</summary>
    [Fact]
    public void AFileWithNoContentTypeIsTextPlain()
    {
        var body = Join(
            "--" + Boundary + "\r\n",
            "Content-Disposition: form-data; name=\"notes\"; filename=\"notes.txt\"\r\n\r\n",
            "hello\r\n",
            "--" + Boundary + "--\r\n"
        );

        Assert.Equal("text/plain", Parse(body).GetFile("notes")!.ContentType);
    }

    [Fact]
    public void RepeatedNamesAccumulate()
    {
        var body = Join(
            "--" + Boundary + "\r\n",
            "Content-Disposition: form-data; name=\"tag\"\r\n\r\n",
            "red\r\n",
            "--" + Boundary + "\r\n",
            "Content-Disposition: form-data; name=\"tag\"\r\n\r\n",
            "blue\r\n",
            "--" + Boundary + "\r\n",
            "Content-Disposition: form-data; name=\"photo\"; filename=\"one.png\"\r\n\r\n",
            "1\r\n",
            "--" + Boundary + "\r\n",
            "Content-Disposition: form-data; name=\"photo\"; filename=\"two.png\"\r\n\r\n",
            "2\r\n",
            "--" + Boundary + "--\r\n"
        );

        var form = Parse(body);

        Assert.Equal("red,blue", form.Get("tag").ToString());
        Assert.Equal(["one.png", "two.png"], form.GetFiles("photo").Select(file => file.FileName));
        Assert.Equal("one.png", form.GetFile("photo")!.FileName);
        Assert.Empty(form.GetFiles("missing"));
        Assert.Null(form.GetFile("missing"));
    }

    [Fact]
    public void AFieldIsDecodedAsUtf8()
    {
        var body = Join(
            "--" + Boundary + "\r\n",
            "Content-Disposition: form-data; name=\"city\"\r\n\r\n",
            "Zürich\r\n",
            "--" + Boundary + "--\r\n"
        );

        Assert.Equal("Zürich", Parse(body).Get("city").ToString());
    }

    [Fact]
    public void AQuotedFileNameIsUnescaped()
    {
        var body = Join(
            "--" + Boundary + "\r\n",
            "Content-Disposition: form-data; name=\"file\"; filename=\"say \\\"hi\\\".txt\"\r\n\r\n",
            "x\r\n",
            "--" + Boundary + "--\r\n"
        );

        Assert.Equal("say \"hi\".txt", Parse(body).GetFile("file")!.FileName);
    }

    [Fact]
    public void AnEmptyFormHasNothingInIt()
    {
        var form = Parse(Encoding.ASCII.GetBytes("--" + Boundary + "--\r\n"));

        Assert.Equal(0, form.Count);
        Assert.Empty(form.Keys);
    }

    public static TheoryData<byte[], string> Malformed() =>
        new()
        {
            { Encoding.ASCII.GetBytes("no boundary anywhere"), "does not contain its boundary" },
            {
                Join(
                    "--" + Boundary + "\r\n",
                    "Content-Disposition: form-data; name=\"a\"\r\n\r\nvalue"
                ),
                "does not end with the boundary"
            },
            { Join("--" + Boundary + "junk\r\n"), "something other than padding" },
            { Join("--" + Boundary), "something other than padding" },
            {
                Join(
                    "--" + Boundary + "\r\n",
                    "Content-Disposition: form-data\r\n\r\n",
                    "x\r\n",
                    "--" + Boundary + "--"
                ),
                "no Content-Disposition name"
            },
            {
                Join("--" + Boundary + "\r\n", "\r\n", "x\r\n", "--" + Boundary + "--"),
                "no Content-Disposition name"
            },
            {
                Join("--" + Boundary + "\r\n", "Content-Disposition: form-data; name=\"a\"\r\n"),
                "headers do not end"
            },
        };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedBodyIsRefusedWithTheReason(byte[] body, string reason)
    {
        var exception = Assert.Throws<FormatException>(() => Parse(body));

        Assert.Contains(reason, exception.Message);
    }

    [Fact]
    public void HeadersLongerThanTheLimitAreRefused()
    {
        var body = Join(
            "--" + Boundary + "\r\n",
            "Content-Disposition: form-data; name=\"a\"\r\n",
            "X-Padding: " + new string('p', MultipartFormParser.MaxHeaderBytes) + "\r\n\r\n",
            "x\r\n",
            "--" + Boundary + "--\r\n"
        );

        Assert.Contains("run past", Assert.Throws<FormatException>(() => Parse(body)).Message);
    }

    [Fact]
    public void MorePartsThanTheLimitAreRefused()
    {
        var parts = new List<object>();

        for (var i = 0; i <= MultipartFormParser.MaxParts; i++)
        {
            parts.Add(
                "--"
                    + Boundary
                    + "\r\nContent-Disposition: form-data; name=\"f"
                    + i
                    + "\"\r\n\r\nx\r\n"
            );
        }

        parts.Add("--" + Boundary + "--\r\n");

        Assert.Contains(
            "more than " + MultipartFormParser.MaxParts + " parts",
            Assert.Throws<FormatException>(() => Parse(Join(parts.ToArray()))).Message
        );
    }
}
