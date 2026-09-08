using Google.Events.Protobuf.Cloud.Firestore.V1;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Gcp.CloudRun.Firestore;

/// <summary>
/// Binds the document as it was before the change.
/// </summary>
/// <remarks>
/// <para>
/// A handler does not normally need this. The body already carries the new document as plain
/// JSON, so <c>void OnOrder(Order order)</c> works and reads like every other handler. This is for
/// a handler that has to know what changed: <c>[OldValue] Order? previous</c> binds the previous
/// document as the handler's own type, through the same deserializer the body goes through;
/// <c>[OldValue] Document? previous</c> binds it in Firestore's own form, with the typed values
/// still on.
/// </para>
/// <para>
/// Null on a create, where there is no previous document, and null on an event whose trigger was
/// not configured to carry it - a deployment decision the handler cannot see, so a handler that
/// needs the previous document should treat null as "not carried" rather than "no previous
/// document". A parameter that cannot hold null asks for something this event does not have.
/// </para>
/// <para>
/// Cosmos on Azure has no previous document at all, so this is the one place the two change feeds
/// diverge; the divergence is documented rather than hidden.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class OldValueAttribute : Attribute, ICustomBindingAttribute {
    public async ValueTask<T> BindValue<T>(IExecutionContext context, IExecutionRequestParameter parameter) {
        if (context.Request is not FirestoreChange change) {
            throw new InvalidOperationException(
                "[OldValue] was bound on a handler that is not serving a Firestore change. It reads " +
                "the document event off the request, so it only works under [Change].");
        }

        var previous = change.OldValue;

        if (previous == null) {
            if (default(T) is null) {
                return default!;
            }

            throw new InvalidCastException(
                $"[OldValue] has no previous document on this {change.Headers["ce-type"]} event, and " +
                $"{typeof(T).Name} cannot hold null.");
        }

        if (previous is T document) {
            return document;
        }

        // The handler's own type, read the way the body is: a fork of the context whose request
        // carries the previous document as JSON, through the pipeline's deserializer, so an AOT
        // application binds it through the same context its body binds through.
        var request = new FirestoreChange(
            change.Method, change.Path, FirestoreValueJson.Body(previous), change.Headers, change.Delivery, change.Event);

        var bound = await context.KnownServices.ContextSerializationService
            .DeserializeRequestBody<T>(context.Clone(request: request));

        return bound!;
    }
}
