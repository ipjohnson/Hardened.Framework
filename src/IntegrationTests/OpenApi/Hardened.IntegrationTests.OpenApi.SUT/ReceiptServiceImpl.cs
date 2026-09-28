using Hardened.IntegrationTests.OpenApi.SUT.Models;
using Hardened.IntegrationTests.OpenApi.SUT.Services;
using Hardened.Requests.Abstract.Attributes;

namespace Hardened.IntegrationTests.OpenApi.SUT;

/// <summary>
/// Implements the interface <c>receipts.yaml</c> generates, and checks nothing itself: a body that
/// breaks the contract and reaches this class is the validation failure under test.
/// </summary>
[Handler]
public class ReceiptServiceImpl : IReceiptService
{
    public Task SyncReceipt(ReceiptSync body) => Task.CompletedTask;

    public Task FileReceipt(ReceiptFiling body) => Task.CompletedTask;
}
