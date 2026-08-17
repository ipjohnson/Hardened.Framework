namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// One document per thing a published description did that this generator got wrong.
/// </summary>
/// <remarks>
/// <para>
/// Every scenario here was found by building against real descriptions - four rounds of ten - and
/// each one cost a download, a build, and a read of several megabytes of generated C# to find. That
/// is a poor way to learn the same lesson twice, so what each round taught is written down here as
/// the smallest document that produces it.
/// </para>
/// <para>
/// The point is not coverage for its own sake. A scenario earns a place here by having broken
/// something, and the comment on it names what: the description it came from and what it did. A
/// document nothing ever got wrong belongs in <c>Specs</c> with the ordinary fixtures.
/// </para>
/// </remarks>
internal static class ScenarioSpecs {

    private const string Head = """
        openapi: "3.1.0"
        info: { title: Scenario, version: "1.0" }
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: getThing
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Thing' }
        components:
          schemas:
        """;

    /// <summary>
    /// OpenAPI 3.1 states nullability as a type array, which the 3.0 line spelled with a flag.
    /// </summary>
    /// <remarks>
    /// OpenAI writes every optional string this way. Read as a choice between two things it turns
    /// each one into a two-branch union - 596 generated types where 92 were real - so what is
    /// asserted is that it stays one nullable property.
    /// </remarks>
    public const string NullableByTypeArray = Head + """

            Thing:
              type: object
              required: [name]
              properties:
                name: { type: [string, "null"] }
                count: { type: [integer, "null"] }
        """;

    /// <summary>
    /// 3.1's <c>const</c>, which is a single-valued enum and is how a great many descriptions spell
    /// a discriminator without declaring one.
    /// </summary>
    public const string ConstProperty = Head + """

            Thing:
              type: object
              properties:
                kind: { type: string, const: thing }
                name: { type: string }
        """;

    /// <summary>
    /// A bound the type its format implies cannot hold.
    /// </summary>
    /// <remarks>
    /// DigitalOcean declares <c>eq_range_index_dive_limit</c> as an integer with a maximum of
    /// 4294967295. Left as <c>int</c> the generated model overflows on a payload the description
    /// calls valid, so the bound is part of the type.
    /// </remarks>
    public const string BoundsWiderThanInt = Head + """

            Thing:
              type: object
              properties:
                small: { type: integer, maximum: 100 }
                wide: { type: integer, maximum: 4294967295 }
        """;

    /// <summary>
    /// A constraint that cannot apply to the type it is written on.
    /// </summary>
    /// <remarks>
    /// <c>minLength</c> on an integer and <c>minimum</c> on a string are both meaningless, and
    /// emitting them produced validator code comparing an int against a string length. The
    /// description is wrong and there is nothing to be done about it, so the constraint is dropped.
    /// </remarks>
    public const string ConstraintsOnMismatchedTypes = Head + """

            Thing:
              type: object
              properties:
                counted: { type: integer, minLength: 3 }
                measured: { type: string, minimum: 5 }
                listed: { type: array, items: { type: string }, minItems: 1 }
        """;

    /// <summary>
    /// A pattern that is not a regular expression any .NET engine will accept.
    /// </summary>
    /// <remarks>
    /// Emitted verbatim it fails at run time inside generated code, where the message names neither
    /// the property nor the description it came from.
    /// </remarks>
    public const string InvalidPattern = Head + """

            Thing:
              type: object
              properties:
                good: { type: string, pattern: "^[a-z]+$" }
                bad: { type: string, pattern: "^(unclosed" }
        """;

    /// <summary>
    /// A value set that reaches C# as nothing at all.
    /// </summary>
    /// <remarks>
    /// Docker and Cloudflare both declare an enum whose members include the empty string; GitHub's
    /// reaction enum is <c>+1</c> and <c>-1</c>; Elasticsearch declares <c>buckets.count</c> beside
    /// <c>buckets_count</c>. Each produced an enum member with no name, or two with the same one.
    /// </remarks>
    public const string AwkwardEnumValues = Head + """

            Thing:
              type: object
              properties:
                reaction: { $ref: '#/components/schemas/Reaction' }
            Reaction:
              type: string
              enum: ["+1", "-1", "", "buckets.count", "buckets_count", "StartTime>"]
        """;

    /// <summary>Binary content, which is a string in the description and bytes in C#.</summary>
    public const string BinaryFormats = Head + """

            Thing:
              type: object
              properties:
                avatar: { type: string, format: byte }
                blob: { type: string, format: binary }
                when: { type: string, format: date-time }
                day: { type: string, format: date }
        """;

    /// <summary>
    /// A schema that refers to itself, and one that nests arrays.
    /// </summary>
    /// <remarks>
    /// A recursive reference is a cycle in every pass that walks references, and each of those had
    /// to be written not to follow it forever.
    /// </remarks>
    public const string RecursiveAndNested = Head + """

            Thing:
              type: object
              properties:
                self: { $ref: '#/components/schemas/Thing' }
                children:
                  type: array
                  items: { $ref: '#/components/schemas/Thing' }
                matrix:
                  type: array
                  items:
                    type: array
                    items: { type: number }
                inline:
                  type: object
                  properties:
                    nested: { type: string }
        """;

    /// <summary>
    /// A request body that is not JSON.
    /// </summary>
    /// <remarks>
    /// An upload is <c>multipart/form-data</c>, and a description that offers only that had its body
    /// read as though the content map were empty.
    /// </remarks>
    public const string MultipartBody = """
        openapi: "3.1.0"
        info: { title: Scenario, version: "1.0" }
        paths:
          /uploads:
            post:
              tags: [Upload]
              operationId: upload
              requestBody:
                content:
                  multipart/form-data:
                    schema:
                      type: object
                      properties:
                        file: { type: string, format: binary }
                        name: { type: string }
              responses:
                '201': { description: created }
        components:
          schemas: {}
        """;

    /// <summary>
    /// 3.1's <c>webhooks</c>, which this generator does not implement.
    /// </summary>
    /// <remarks>
    /// Here so that it is known what happens rather than assumed: a document carrying them parses,
    /// and the operations it does declare are unaffected. If webhooks are ever generated, this is
    /// the document that says what changed.
    /// </remarks>
    public const string Webhooks = """
        openapi: "3.1.0"
        info: { title: Scenario, version: "1.0" }
        webhooks:
          thingCreated:
            post:
              operationId: onThingCreated
              requestBody:
                content:
                  application/json:
                    schema: { $ref: '#/components/schemas/Thing' }
              responses:
                '200': { description: ok }
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: getThing
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Thing' }
        components:
          schemas:
            Thing:
              type: object
              properties:
                name: { type: string }
        """;
}
