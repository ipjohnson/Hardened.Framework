using Hardened.Requests.Abstract.Execution;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Requests.Runtime.Serializer;

public static class RawOutputHelper {
    public static DefaultOutputFunc OutputFunc(string contentType) =>
        context => DefaultRawOutput(contentType, context);

    private static async Task DefaultRawOutput(string contentType, IExecutionContext executionContext) {
        executionContext.Response.ContentType = contentType;

        var outputValue = executionContext.Response.ResponseValue;

        if (outputValue is string stringValue) {
            var bytes = Encoding.UTF8.GetBytes(stringValue);

            await executionContext.Response.Body.WriteAsync(bytes, 0, bytes.Length, executionContext.CancellationToken);
        }
        else if (outputValue is byte[] bytes) {
            await executionContext.Response.Body.WriteAsync(bytes, 0, bytes.Length, executionContext.CancellationToken);
        }
        else if (outputValue is Stream stream) {
            await stream.CopyToAsync(executionContext.Response.Body, executionContext.CancellationToken);
        }
        else if (outputValue == null) {
            executionContext.Response.Body.Close();
        }
        else {
            throw new Exception("Unsupported raw type, must be string, byte[], or Stream");
        }
    }
}