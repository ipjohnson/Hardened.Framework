using Hardened.IntegrationTests.WebApp.SUT.Models;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Attributes;
using IFormFile = Hardened.Requests.Abstract.Forms.IFormFile;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// Handlers binding from an <c>application/x-www-form-urlencoded</c> body.
/// </summary>
/// <remarks>
/// The wire format is a query string in the body, so what is worth covering here is the places the
/// two differ or where the body being a stream matters - the <c>+</c> that means a space, a field
/// sent more than once, and a request that sends no form at all.
/// </remarks>
[BasePath("/form")]
public class FormController
{
    /// <summary>Two fields, the ordinary case.</summary>
    [Post("/sign-in")]
    public string SignIn([FromForm] string username, [FromForm] string password) =>
        username + ":" + password;

    /// <summary>A field converted to something that is not a string.</summary>
    [Post("/quantity")]
    public int Quantity([FromForm] int count) => count * 2;

    /// <summary>A renamed field, so the parameter and the wire name can differ.</summary>
    [Post("/renamed")]
    public string Renamed([FromForm("user_name")] string userName) => userName;

    /// <summary>
    /// An absent field with a default, and an optional one without.
    /// </summary>
    /// <remarks>
    /// The same conversion path every other string-valued source uses, so a missing form field
    /// behaves like a missing query parameter rather than throwing.
    /// </remarks>
    [Post("/optional")]
    public string Optional([FromForm] string present, [FromForm] string missing = "fallback") =>
        present + ":" + missing;

    /// <summary>The eight fields of RequestBench's form, bound as one model and echoed.</summary>
    [Post("/search")]
    public SearchForm Search([FromForm] SearchForm search) => search;

    /// <summary>A model written as a class, with each kind of member the binder handles.</summary>
    [Post("/profile")]
    public string Profile([FromForm] ProfileForm profile) =>
        profile.DisplayName
        + ":"
        + profile.Age
        + ":"
        + profile.Theme
        + ":"
        + string.Join(",", profile.Interests ?? []);

    /// <summary>
    /// RequestBench's <c>forms.multipart</c>: two fields and a file, answered with the file's name
    /// and length and the fields echoed.
    /// </summary>
    [Post("/upload")]
    public Uploaded Upload(
        [FromForm] string tenant,
        [FromForm] string requestId,
        [FromForm] IFormFile file
    ) => new(new UploadedFile(file.FileName, file.Length), new UploadEcho(tenant, requestId));

    /// <summary>The same parts bound to one model.</summary>
    [Post("/upload-model")]
    public Uploaded UploadModel([FromForm] UploadForm upload) =>
        new(
            new UploadedFile(upload.File.FileName, upload.File.Length),
            new UploadEcho(upload.Tenant, upload.RequestId)
        );

    /// <summary>Every file sent under one name, and an optional one beside them.</summary>
    [Post("/upload-many")]
    public string UploadMany(
        [FromForm] IReadOnlyList<IFormFile> photos,
        [FromForm] IFormFile? thumbnail
    ) =>
        string.Join(",", photos.Select(photo => photo.FileName + ":" + photo.Length))
        + "|"
        + (thumbnail?.FileName ?? "none");

    /// <summary>A file's bytes, answered as they arrived.</summary>
    [Post("/upload-echo")]
    [Produces("application/octet-stream")]
    public byte[] UploadEcho([FromForm] IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var copy = new MemoryStream();

        stream.CopyTo(copy);

        return copy.ToArray();
    }
}
