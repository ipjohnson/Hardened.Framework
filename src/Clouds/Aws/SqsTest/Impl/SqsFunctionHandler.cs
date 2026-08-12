using Hardened.Requests.Abstract.Attributes;

namespace SqsTest.Impl;

public class SqsFunctionHandler {
    private readonly CountingService _countingService;

    public SqsFunctionHandler(CountingService countingService) {
        _countingService = countingService;
    }

    [HardenedFunction]
    public async Task Process(DataModel dataModel) {
        _countingService.Count++;
    }
}