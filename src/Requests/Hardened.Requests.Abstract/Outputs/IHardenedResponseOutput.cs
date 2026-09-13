using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Outputs;

/// <summary>
/// Something that writes a response itself, instead of the response being serialized.
/// </summary>
/// <remarks>
/// <para>
/// One question and nothing else: write this response. Everything a particular kind of output needs
/// beyond that - a model, a content type, a writer, a layout - belongs to the base class that kind
/// of output is built on, not here. A view is the obvious implementation; a signed file, a
/// server-sent event stream and a protobuf frame are all the same shape.
/// </para>
/// <para>
/// <b>An output is not a serializer, and does not negotiate.</b> A handler that declares one has
/// said what its response <em>is</em>, and that is what the client gets whatever it asked for. The
/// alternative was refusing an <c>Accept</c> the output does not answer with a <c>406</c>, which
/// cost every generated client its call: a document generator reads the operation and pins
/// <c>Accept: application/json</c>, and each of those requests was refused by the one route whose
/// author had written a view for it.
/// </para>
/// <para>
/// Falling back to serializing the model instead is the other thing this must not do. A view
/// usually renders a subset of what its model holds, so a fallback would put the rest of it on the
/// wire: the difference between a page showing a customer's name and a response carrying their
/// address, their internal identifiers and whatever else the model was carrying. Adding
/// <c>[Output&lt;T&gt;]</c> to a handler can never widen what it discloses.
/// </para>
/// </remarks>
public interface IHardenedResponseOutput
{
    /// <summary>
    /// Writes the response: the content type, the headers it needs, and the body.
    /// </summary>
    /// <remarks>
    /// The value the handler returned is on <c>context.Response.ResponseValue</c>. Taking it from
    /// there rather than being handed it is what keeps this interface to one method - and what
    /// lets an output that needs no model implement it without an unused parameter.
    /// </remarks>
    Task WriteOutput(IExecutionContext context);
}

/// <summary>
/// An output over a particular model type.
/// </summary>
/// <remarks>
/// <para>
/// It declares no members of its own, and it is not decoration. A generated assignment
/// </para>
/// <code>
/// private static readonly IHardenedResponseOutput&lt;FortunePage&gt; _outputCheck_GetFortunes = new Views.Fortunes();
/// </code>
/// <para>
/// is the one thing that makes "this view's model matches this handler's return type" a compile
/// error, across a generator boundary where nothing can inspect the other generator's output. The
/// type parameter exists to be bound against, and a marker interface is the smallest thing that
/// does that.
/// </para>
/// </remarks>
/// <typeparam name="TModel">
/// Contravariant, so an output written against a base model serves a handler returning a derived
/// one.
/// </typeparam>
public interface IHardenedResponseOutput<in TModel> : IHardenedResponseOutput;
