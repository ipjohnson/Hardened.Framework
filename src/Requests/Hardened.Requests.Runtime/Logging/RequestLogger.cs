﻿using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Microsoft.Extensions.Logging;

namespace Hardened.Requests.Runtime.Logging;

public partial class RequestLogger : IRequestLogger {
    private static readonly TimeSpan _emptyTimeSpan = new(0);
    private readonly ILogger<RequestLogger> _logger;

    public RequestLogger(ILogger<RequestLogger> logger) {
        _logger = logger;
    }

    public void RequestBegin(IExecutionContext context) {
        LogRequestStarted(context.Request.Method, context.Request.Path);
    }

    public void RequestMapped(IExecutionContext context) {
        LogRequestMapped(context.Request.Method, context.Request.Path, context.HandlerInfo!.HandlerType.Name,
             context.HandlerInfo!.InvokeMethod);
    }

    public void RequestEnd(IExecutionContext context) {
        var currentTime = DateTime.Now;

        LogRequestFinished(
            context.Request.Method,
            context.Request.Path,
            context.Response.Status,
            context.StartTime.GetElapsedTime()
        );
    }

    public void RequestParameterBindFailed(IExecutionContext context, Exception? exp) {
        _logger.LogError(exp, "{method} {path} failed to bind parameters",
            context.Request.Method, context.Request.Path);
    }

    public void RequestFailed(IExecutionContext context, Exception exp) {
        _logger.LogError(exp, "{method} {path} request failed", context.Request.Method, context.Request.Path);
    }

    public void ResourceNotFound(IExecutionContext context) {
        LogResourceNotFound(context.Request.Method, context.Request.Path);
    }

    [LoggerMessage(
        EventId = 78000,
        Level = LogLevel.Information,
        Message = "{httpMethod} {path} started")]
    protected partial void LogRequestStarted(string httpMethod, string path);

    [LoggerMessage(
        EventId = 78001,
        Level = LogLevel.Information,
        Message = "{httpMethod} {path} mapped to {typeName}.{methodName}")]
    protected partial void LogRequestMapped(string httpMethod, string path, string typeName, string methodName);

    [LoggerMessage(
        EventId = 78002,
        Level = LogLevel.Information,
        Message = "{httpMethod} {path}  finished status code '{statusCode}'  duration {durationMs}")]
    protected partial void LogRequestFinished(
        string httpMethod, string path, int? statusCode, TimeSpan durationMs);

    [Microsoft.Extensions.Logging.LoggerMessage(
        EventId = 78003,
        Level = LogLevel.Information,
        Message = "{httpMethod} {path} Resource Not Found")]
    protected partial void LogResourceNotFound(string httpMethod, string path);
}