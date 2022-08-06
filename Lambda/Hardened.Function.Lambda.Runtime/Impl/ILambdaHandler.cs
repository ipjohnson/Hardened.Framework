﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Hardened.Shared.Runtime.Application;

namespace Hardened.Function.Lambda.Runtime.Impl
{
    public interface ILambdaHandler : IApplicationRoot
    {
        Task<Stream> Invoke(Stream input, ILambdaContext lambdaContext);
    }

    public interface ILambdaHandler<TRequest, TResponse> : ILambdaHandler
    {

    }


    public interface ILambdaHandler<in TResponse> : ILambdaHandler
    {

    }
}