using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.Amz.Function.Lambda.Runtime.Execution;

public class LambdaExecutionResponse : IExecutionResponse
{
    public LambdaExecutionResponse(Stream body, IHeaderCollection headers)
    {
        Body = body;
        Headers = headers;
    }

    public object Clone()
    {
        throw new NotImplementedException();
    }

    public string? ContentType { get; set; }

    public object? ResponseValue { get; set; }

    public string? TemplateName { get; set; }

    public int? Status { get; set; }

    public bool ShouldCompress { get; set; }

    public Stream Body { get; set; }

    public IHeaderCollection Headers { get; }

    public Exception? ExceptionValue { get; set; }

    public bool ResponseStarted { get; }

    public bool IsBinary { get; set; }

    public bool ShouldSerialize { get; set; } = true;
}