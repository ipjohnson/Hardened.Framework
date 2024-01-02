using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Execution;

public interface IExecutionResponse : ICloneable {
    string? ContentType { get; set; }

    object? ResponseValue { get; set; }

    string? TemplateName { get; set; }

    int? Status { get; set; }

    bool ShouldCompress { get; set; }

    Stream Body { get; set; }

    IDictionary<string, StringValues> Headers { get; }

    Exception? ExceptionValue { get; set; }

    bool ResponseStarted { get; }

    bool IsBinary { get; set; }

    ICookieSetCollection Cookies { get; }

    bool ShouldSerialize { get; set; }
}