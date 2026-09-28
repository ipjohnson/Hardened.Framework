using Hardened.Requests.Runtime.Validation;
using Hardened.Web.Runtime.Responses;

namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// Constraints a request can reach only through a nested model, over a real request.
/// </summary>
/// <remarks>
/// <para>
/// A model whose only constrained member was another model counted as having nothing to validate.
/// A body of that shape reached the handler unvalidated: a nested <c>payload</c> that the contract
/// declares required arrived <c>null</c> and failed in the handler with a 500. A member of that
/// shape was never descended into, so the outer model's validator stopped at it.
/// </para>
/// <para>
/// Bodies are raw JSON rather than typed objects, because a member sent as <c>null</c> or left out
/// is part of what is under test.
/// </para>
/// </remarks>
public class NestedOnlyConstraintTests
{
    private static async Task<RequestValidationFieldError> Rejected(
        ITestWebApp app,
        string path,
        string body,
        string field
    )
    {
        var response = await app.Post(body, path);

        response.Assert.BadRequest();

        var error = response.Deserialize<RequestValidationError>();

        Assert.NotNull(error);
        Assert.Equal("ValidationError", error.Type);

        return Assert.Single(error.Errors!, e => e.Field == field);
    }

    [ModuleTest]
    public async Task ABodyWithinItsNestedBoundIsAccepted(ITestWebApp app)
    {
        var response = await app.Post("""{"receipt":{"payload":"ok"}}""", "/receipts/sync");

        Assert.Equal(204, response.StatusCode);
    }

    /// <summary>
    /// The nested model is optional, so leaving it out is not a failure.
    /// </summary>
    [ModuleTest]
    public async Task ABodyWithoutTheNestedModelIsAccepted(ITestWebApp app)
    {
        var response = await app.Post("{}", "/receipts/sync");

        Assert.Equal(204, response.StatusCode);
    }

    /// <summary>
    /// The case that failed in the handler: a nested member the contract requires, sent as
    /// <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <c>null</c> rather than absent. The generated reader already refuses an absent required
    /// member while it binds the body, validator or not. A <c>null</c> passes that check, and only
    /// the validator's <c>[Required]</c> refuses it.
    /// </remarks>
    [ModuleTest]
    public async Task ANestedRequiredMemberIsCheckedOnABodyWithNoConstraintOfItsOwn(ITestWebApp app)
    {
        var field = await Rejected(
            app,
            "/receipts/sync",
            """{"receipt":{"payload":null}}""",
            "body.receipt.payload"
        );

        Assert.Equal("required", field.Code);
    }

    [ModuleTest]
    public async Task ANestedBoundIsCheckedOnABodyWithNoConstraintOfItsOwn(ITestWebApp app)
    {
        var field = await Rejected(
            app,
            "/receipts/sync",
            """{"receipt":{"payload":"0123456789abcdefX"}}""",
            "body.receipt.payload"
        );

        Assert.Equal("string_length", field.Code);
    }

    [ModuleTest]
    public async Task AFilingWithinItsBoundsIsAccepted(ITestWebApp app)
    {
        var response = await app.Post(
            """{"name":"q3","folder":{"receipt":{"payload":"ok"}}}""",
            "/receipts/filings"
        );

        Assert.Equal(204, response.StatusCode);
    }

    /// <summary>
    /// <c>folder</c>'s model has no constraint of its own, only a nested model that does, and the
    /// filing's validator descends through it.
    /// </summary>
    /// <remarks>
    /// The field is <c>body...receipt.payload</c> rather than <c>body.folder.receipt.payload</c>.
    /// ValidationModules renders a path three or more descents deep as its outermost segment and
    /// the error's parent, and <c>...</c> marks the segments it left out.
    /// </remarks>
    [ModuleTest]
    public async Task AMemberWhoseModelOnlyNestsConstraintsIsDescendedInto(ITestWebApp app)
    {
        var field = await Rejected(
            app,
            "/receipts/filings",
            """{"name":"q3","folder":{"receipt":{"payload":"0123456789abcdefX"}}}""",
            "body...receipt.payload"
        );

        Assert.Equal("string_length", field.Code);
    }
}
