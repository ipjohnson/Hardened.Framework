using Amazon.Lambda.DynamoDBEvents;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Aws.Lambda.DynamoDb;

/// <summary>
/// Binds the row as it is after the change, in DynamoDB's own wire form.
/// </summary>
/// <remarks>
/// <para>
/// A handler does not normally need this. The body already carries the new image with the type
/// wrappers stripped off, so <c>void OnOrder(Order order)</c> works and reads like every other
/// handler. This is for the cases that form loses: telling an absent attribute from a null one,
/// reading a set, or handling an item whose shape is not known at compile time.
/// </para>
/// <para>
/// Null on a REMOVE, where there is no new image. Bind <see cref="OldImageAttribute"/> for what was
/// deleted.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public class NewImageAttribute : Attribute, ICustomBindingAttribute {
    public ValueTask<T> BindValue<T>(IExecutionContext context, IExecutionRequestParameter parameter) =>
        Image.Bind<T>(context, change => change.NewImage, "NewImage");
}

/// <summary>
/// Binds the row as it was before the change, in DynamoDB's own wire form.
/// </summary>
/// <remarks>
/// Null on an INSERT, and null on every record of a stream configured <c>NEW_IMAGE</c> or
/// <c>KEYS_ONLY</c> - which is a deployment decision the handler cannot see, so a handler that needs
/// the previous row should treat null as "not configured for this" rather than "no previous row".
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public class OldImageAttribute : Attribute, ICustomBindingAttribute {
    public ValueTask<T> BindValue<T>(IExecutionContext context, IExecutionRequestParameter parameter) =>
        Image.Bind<T>(context, change => change.OldImage, "OldImage");
}

/// <summary>
/// What both image attributes do, which is read one off the request.
/// </summary>
/// <remarks>
/// The image comes off <see cref="DynamoDbChange"/> rather than out of an injected singleton
/// holding "the record being handled". <c>Hardened.Amz</c> did the latter, and it is correct only
/// while exactly one record is in flight.
/// </remarks>
internal static class Image {
    public static ValueTask<T> Bind<T>(
        IExecutionContext context,
        Func<DynamoDbChange, IDictionary<string, DynamoDBEvent.AttributeValue>?> select,
        string name) {
        if (context.Request is not DynamoDbChange change) {
            throw new InvalidOperationException(
                $"[{name}] was bound on a handler that is not serving a DynamoDB change. It reads " +
                "the stream record off the request, so it only works under [Change].");
        }

        if (select(change) is T value) {
            return new ValueTask<T>(value);
        }

        // Null is a legitimate answer - no new image on a REMOVE, no old image on an INSERT - so a
        // nullable target gets it rather than an exception. A non-nullable one asked for something
        // this record does not have.
        if (default(T) is null) {
            return new ValueTask<T>(default(T)!);
        }

        throw new InvalidCastException(
            $"[{name}] binds IDictionary<string, DynamoDBEvent.AttributeValue>, and this parameter is " +
            $"{typeof(T).Name}.");
    }
}
