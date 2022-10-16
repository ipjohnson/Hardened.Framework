﻿using Hardened.Requests.Abstract.Execution;

namespace Hardened.Amz.Function.Lambda.Runtime.Impl;

public class LambdaInvokeFilter : IExecutionFilter
{
    private readonly IExecutionRequestHandler _executionRequestHandler;
        
    public LambdaInvokeFilter(IExecutionRequestHandler executionRequestHandler)
    {
        _executionRequestHandler = executionRequestHandler;
    }

    public async Task Execute(IExecutionChain chain)
    {
        await _executionRequestHandler.GetExecutionChain(chain.Context).Next();
    }
}