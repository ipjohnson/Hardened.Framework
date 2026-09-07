namespace Hardened.Functions.Runtime.Attributes;

/// <summary>
/// Routes changes to a table's rows to the attributed handler.
/// </summary>
/// <remarks>
/// <para>
/// DynamoDB Streams on AWS, the Cosmos DB change feed on Azure, Firestore triggers on Google. The
/// handler names the table and nothing else; which adapter delivers to it is decided by the runtime
/// package the project references, through the <c>HardenedChangeModule</c> build property that
/// package declares.
/// </para>
/// <para>
/// <b>Separate from <see cref="StreamAttribute"/>, which is the other ordered source.</b> A change
/// feed carries a row before and after an edit, so a handler binds an image of the item and can ask
/// what the edit was. A stream carries records a publisher wrote, which are opaque until the
/// handler decodes them. The two are the same delivery shape and different vocabularies, and one
/// adapter package serves each - two packages binding a single property would resolve
/// first-import-wins.
/// </para>
/// <para>
/// Routes as <c>CHANGE /orders</c>. Ordered per partition key and replayable, so a delivery is a
/// position in a log rather than a set of independent messages: see
/// <c>BatchFailureMode.Checkpoint</c> for what that means when one item fails.
/// </para>
/// </remarks>
public class ChangeAttribute : Attribute {
    public ChangeAttribute(string name) {
        Name = name;
    }

    /// <summary>
    /// The table's own name, not an ARN and not a stream ARN.
    /// </summary>
    /// <remarks>
    /// A stream's address contains a timestamp, so it changes when a table's stream is disabled and
    /// re-enabled and would make a handler's route a deployment detail. The table name does not
    /// move.
    /// </remarks>
    public string Name { get; }
}
