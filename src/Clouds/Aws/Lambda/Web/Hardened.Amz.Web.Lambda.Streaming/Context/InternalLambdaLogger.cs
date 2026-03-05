using Amazon.Lambda.Core;

namespace Hardened.Amz.Web.Lambda.Streaming.Context;

internal class InternalLambdaLogger : ILambdaLogger {
    public void Log(string message) {
        Console.Write(message);
    }

    public void LogLine(string message) {
        Console.WriteLine(message);
    }
}
