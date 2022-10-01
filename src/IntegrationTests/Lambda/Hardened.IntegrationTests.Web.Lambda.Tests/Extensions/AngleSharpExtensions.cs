using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Hardened.Web.Testing;

namespace Hardened.IntegrationTests.Web.Lambda.Tests.Extensions;

public static class AngleSharpExtensions
{
    public static async Task<IHtmlDocument> ParseDocument(this TestWebResponse response)
    {
        using var streamReader = new StreamReader(response.Body);
        var documentString = await streamReader.ReadToEndAsync();
        var parser = new HtmlParser();

        return await parser.ParseDocumentAsync(documentString);
    }
}