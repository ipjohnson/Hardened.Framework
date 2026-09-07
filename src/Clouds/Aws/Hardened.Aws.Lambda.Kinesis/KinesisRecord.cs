namespace Hardened.Aws.Lambda.Kinesis;

/// <summary>
/// One record off a shard, read out of the envelope rather than bound to a model.
/// </summary>
/// <param name="Data">
/// The publisher's own bytes, base64-decoded. Kinesis carries an opaque blob and says nothing about
/// what is in it, so this is the whole of what a handler binds - the same arrangement a direct
/// invocation has, and the reason this adapter needs no serializer context.
/// </param>
/// <param name="SequenceNumber">
/// The record's position in the shard. What a failure report names, and the only identifier Lambda
/// can resolve a checkpoint from.
/// </param>
/// <param name="PartitionKey">
/// What the publisher chose to order by. Two records with one partition key are delivered in the
/// order they were written, which is the promise <c>BatchFailureMode.Checkpoint</c> exists to keep.
/// </param>
/// <param name="EventId">
/// <c>shardId-000000000000:49590338271490256608559692538361571095921575989136588898</c>. Carries the
/// shard, which the sequence number alone does not.
/// </param>
/// <param name="ArrivalTime">
/// When Kinesis accepted the record, as the epoch seconds the envelope carries.
/// </param>
/// <remarks>
/// A record rather than a model from <c>Amazon.Lambda.KinesisEvents</c>, and that is a deliberate
/// dependency decision. That package is 27.6 KB and depends on <c>AWSSDK.Kinesis</c> and
/// <c>AWSSDK.Core</c>, which are 1094 KB between them - three times the entire Hardened framework
/// after trimming, to name five fields that are documented and stable. The EventBridge adapter made
/// the same call for the same reason.
/// </remarks>
public sealed record KinesisRecord(
    ReadOnlyMemory<byte> Data,
    string SequenceNumber,
    string PartitionKey,
    string EventId,
    string? ArrivalTime);
