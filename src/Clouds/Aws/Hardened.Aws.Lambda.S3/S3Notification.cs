namespace Hardened.Aws.Lambda.S3;

/// <summary>
/// One object notification, read out of the envelope rather than bound to a model.
/// </summary>
/// <param name="Key">
/// The object's key, URL-decoded. S3 encodes it in the notification and a handler that used the raw
/// value would fetch the wrong object for every key containing a space or a non-ASCII character -
/// see <see cref="S3Adapter"/> for why a plus sign is the part that catches people.
/// </param>
/// <param name="Size">
/// The object's size in bytes, or null. Absent on a delete, because there is no object left to
/// have one.
/// </param>
/// <param name="ETag">The object's entity tag, or null on a delete.</param>
/// <param name="Sequencer">
/// How two notifications for one key are ordered. S3 does not deliver notifications in order and
/// says so; this is a hexadecimal string that compares lexicographically, and it is the only way to
/// tell a stale notification from a current one.
/// </param>
/// <param name="EventName">
/// <c>ObjectCreated:Put</c>, <c>ObjectRemoved:Delete</c> and the rest, as S3 spells them.
/// </param>
/// <param name="EventTime">When S3 recorded the change, as the ISO 8601 string it sent.</param>
/// <remarks>
/// A record rather than a model from <c>Amazon.Lambda.S3Events</c>. That package has no
/// dependencies and would have been safe to take, unlike the Kinesis one - it is left out because
/// six documented fields do not need 27.1 KB of models to name them, and because the handler binds
/// a flattened projection either way.
/// </remarks>
public sealed record S3Notification(
    string Key,
    long? Size,
    string? ETag,
    string? Sequencer,
    string EventName,
    string? EventTime);
