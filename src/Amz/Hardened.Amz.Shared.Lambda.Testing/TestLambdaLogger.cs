﻿using Amazon.Lambda.Core;

namespace Hardened.Amz.Shared.Lambda.Testing;

public class TestLambdaLogger : ILambdaLogger
{
    public void Log(string message)
    {
    }

    public void LogLine(string message)
    {
    }

    public void Log(string level, string message)
    {
    }

    public void Log(LogLevel level, string message)
    {
    }

    public void LogTrace(string message)
    {
    }

    public void LogDebug(string message)
    {
    }

    public void LogInformation(string message)
    {
    }

    public void LogWarning(string message)
    {
    }

    public void LogError(string message)
    {
    }

    public void LogCritical(string message)
    {
    }
}