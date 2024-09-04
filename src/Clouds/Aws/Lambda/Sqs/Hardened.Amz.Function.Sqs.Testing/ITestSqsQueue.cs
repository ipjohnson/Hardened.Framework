namespace Hardened.Amz.Function.Sqs.Testing;


public interface ITestSqsQueue {
    Task SendMessage<T>(params T[] messages);
}