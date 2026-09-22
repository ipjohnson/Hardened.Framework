using IFormFile = Hardened.Requests.Abstract.Forms.IFormFile;

namespace Hardened.IntegrationTests.WebApp.SUT.Models;

/// <summary>What RequestBench's <c>forms.multipart</c> test answers about the file.</summary>
public record UploadedFile(string Name, long Bytes);

/// <summary>And the two fields it sent beside it, echoed.</summary>
public record UploadEcho(string Tenant, string RequestId);

/// <summary>RequestBench's <c>forms.multipart</c> answer.</summary>
public record Uploaded(UploadedFile File, UploadEcho Echo);

/// <summary>The same three parts, bound as one model.</summary>
public record UploadForm(string Tenant, string RequestId, IFormFile File);
