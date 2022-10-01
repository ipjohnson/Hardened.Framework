using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.Requests.Testing;

public class TestExecutionResponse : IExecutionResponse
{
    public TestExecutionResponse(Stream body)
    {
        Body = body;
    }

    public object Clone()
    {
        throw new NotImplementedException();
    }

    public string? ContentType
    {
        get => Headers.Get("Content-Type"); 
        set => Headers.Set("Content-Type", value);
    }

    public object? ResponseValue { get; set; }

    public string? TemplateName { get; set; }

    public int? Status { get; set; }

    public bool ShouldCompress { get; set; }
        
    public Stream Body { get; set; }

    public IHeaderCollection Headers { get; set; }

    public Exception? ExceptionValue { get; set; }

    public bool ResponseStarted => Body.Position > 0;

    public bool IsBinary { get; set; }

    public bool ShouldSerialize { get; set; } = true;
}