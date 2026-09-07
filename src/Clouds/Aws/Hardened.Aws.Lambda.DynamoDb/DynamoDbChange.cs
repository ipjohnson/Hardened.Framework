using Amazon.Lambda.DynamoDBEvents;
using Hardened.Aws.Lambda.Runtime.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.DynamoDb;

/// <summary>
/// One change to one row, with the record it came from still attached.
/// </summary>
/// <remarks>
/// <para>
/// The record rides on the request rather than in an injected singleton, which is what
/// <c>Hardened.Amz</c> did through <c>CurrentDdbRecordContext</c>. A singleton holding "the record
/// being handled" is correct only while exactly one is in flight, and it makes a test that wants two
/// set up global state; a fork already exists per record, so the record belongs on it.
/// </para>
/// <para>
/// This is what <c>[NewImage]</c> and <c>[OldImage]</c> read.
/// </para>
/// </remarks>
public class DynamoDbChange : LambdaPayloadRequest {
    public DynamoDbChange(
        string method,
        string path,
        Stream body,
        IDictionary<string, StringValues> headers,
        DynamoDBEvent.DynamodbStreamRecord record)
        : base(method, path, body, headers) {
        Record = record;
    }

    /// <summary>The stream record this request was built from.</summary>
    public DynamoDBEvent.DynamodbStreamRecord Record { get; }

    /// <summary>The row as it is now, or null for a REMOVE.</summary>
    public IDictionary<string, DynamoDBEvent.AttributeValue>? NewImage => Record.Dynamodb?.NewImage;

    /// <summary>
    /// The row as it was, or null for an INSERT and for a stream that carries only new images.
    /// </summary>
    public IDictionary<string, DynamoDBEvent.AttributeValue>? OldImage => Record.Dynamodb?.OldImage;
}
