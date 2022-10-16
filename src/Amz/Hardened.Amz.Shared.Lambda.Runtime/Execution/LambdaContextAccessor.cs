﻿using Amazon.Lambda.Core;

namespace Hardened.Amz.Shared.Lambda.Runtime.Execution;

public interface ILambdaContextAccessor
{
    ILambdaContext? Context { get; set; }
}

public class LambdaContextAccessor : ILambdaContextAccessor
{
    public ILambdaContext? Context { get; set; }
}