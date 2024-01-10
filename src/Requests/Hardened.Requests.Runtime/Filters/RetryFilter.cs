using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Utilities;

namespace Hardened.Requests.Runtime.Filters;

public class RetryFilter : IExecutionFilter {
    private IMemoryStreamPool _memoryStreamPool;
    private int _retryCount;
    private int _retrySleepTime;
    
    public RetryFilter(IMemoryStreamPool memoryStreamPool, int retryCount, int retrySleepTime) {
        _memoryStreamPool = memoryStreamPool;
        _retryCount = retryCount;
        _retrySleepTime = retrySleepTime;
    }

    public async Task Execute(IExecutionChain chain) {
        await using var replayBuffer = new MemoryStreamPoolWrapper(_memoryStreamPool.Get());

        await chain.Context.Request.Body.CopyToAsync(replayBuffer);

        bool success = false;
        Exception? retryException = null;
        
        for (var i = 0; i < _retryCount && !success; i++) {
            replayBuffer.Position = 0;
            
            try {
                chain.Context.Request.Body = replayBuffer;

                await chain.Next();
                
                success = true;
            }
            catch (Exception exp) {
                retryException = exp;
                await Task.Delay(_retrySleepTime);
            }
        }

        if (!success && retryException != null) {
            throw retryException;
        }
    }
}