using System;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A non-2xx response the handler can answer by throwing.
/// </summary>
/// <remarks>
/// <para>
/// Hardened has three ways to answer with an error, and this is the declaration for the first of
/// them. A handler returning <c>Response&lt;Todo, NotFound&gt;</c> or a union has already declared
/// its responses in its signature, and the document is written from the return type. A handler that
/// throws has not: the throw is a statement in a method body, and nothing about the signature says
/// it can happen.
/// </para>
/// <code>
/// [Get("/pets/{petId}")]
/// [Throws&lt;RateLimited&gt;]
/// public Task&lt;Pet&gt; GetPet(string petId) { ... }
/// </code>
/// <para>
/// <b>The status comes from the type, not from an argument.</b> <c>RateLimited</c> carries
/// <c>[HttpStatus(429)]</c>, which is the same thing <c>Response&lt;Pet, RateLimited&gt;</c> reads
/// to know what a case answers. One vocabulary across all three models: the type names the status,
/// and only the delivery differs. A type carrying no <c>[HttpStatus]</c> has to say which status it
/// means, and the generator refuses a declaration that names neither.
/// </para>
/// <para>
/// <b>One declaration it also decides.</b> <c>[Throws&lt;RequestValidationError&gt;(422)]</c> says
/// the operation answers validation failures with 422, so that is the status the runtime answers
/// them with - the filter's refusal, a handler's own <c>ValidationException</c>, and a body the
/// deserializer could not read alike. A described operation declaring 422 has behaved this way
/// since 0.18; this is how a hand-written one says it, without a second place to write it down.
/// </para>
/// <para>
/// <b>What this deliberately does not promise.</b> It does not assert that the handler throws this,
/// nor that it throws nothing else. An unmapped exception is unplanned by definition and the runtime
/// already has somewhere to put it. Verifying completeness is the road ASP.NET went down with its
/// MVC API analyzers, and it abandoned it in .NET 10 — not because the check was wrong but because
/// the typed alternative made it unnecessary. That alternative is <c>Response</c> and union mode,
/// which are right here. This is for the model that has no type to read.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ThrowsAttribute<TError> : Attribute {

    /// <summary>Declares a thrown error whose type carries its own <c>[HttpStatus]</c>.</summary>
    public ThrowsAttribute() { }

    /// <summary>
    /// Declares a thrown error and states its status, for a type that does not carry one.
    /// </summary>
    public ThrowsAttribute(int statusCode) => StatusCode = statusCode;

    /// <summary>The status stated here, or null to take it from the type.</summary>
    public int? StatusCode { get; }

    /// <summary>Overrides the description written into the document for this response.</summary>
    public string? Description { get; set; }
}
