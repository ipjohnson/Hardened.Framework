﻿using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Runtime.Application
{
    public interface IApplicationRoot
    {
        IServiceProvider Provider { get; }
    }
}