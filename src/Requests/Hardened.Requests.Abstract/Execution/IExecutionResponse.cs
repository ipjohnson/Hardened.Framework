using Hardened.Requests.Abstract.Headers;

namespace Hardened.Requests.Abstract.Execution
{
    public interface IExecutionResponse : ICloneable
    {
        string? ContentType { get; set; }

        object? ResponseValue { get; set; }

        string? TemplateName { get; set; }
        
        int? Status { get; set; }
        
        bool ShouldCompress { get; set; }

        Stream Body { get; set; }

        IHeaderCollection Headers { get; }

        Exception? ExceptionValue { get; set; }

        bool ResponseStarted { get; }

        bool IsBinary { get; set; }

        bool ShouldSerialize { get; set; }
    }
}
