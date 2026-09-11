using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Serializer;

public interface ISerializationLocatorService {
    IRequestDeserializer FindRequestDeserializer(IExecutionContext context);

    IResponseSerializer FindResponseSerializer(IExecutionContext context);

    /// <summary>
    /// The serializer registered for <paramref name="contentType"/>, or null where nothing writes
    /// it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A lookup by tag rather than a search over predicates, and the thing that makes a declared
    /// content type resolvable before any request arrives. <c>IOFilterProvider</c> asks this once as
    /// a handler's pipeline is composed.
    /// </para>
    /// <para>
    /// <b>The last registration under a content type is the answer.</b> A module the application
    /// imports is applied after the framework module it depends on, so installing a package that
    /// replaces JSON is what makes it the JSON serializer. That used to be an <c>Order</c>, because
    /// within a module DependencyModules sorts by implementation type name and an application could
    /// not steer it; a tag makes it a rule instead of a race between two class names.
    /// </para>
    /// </remarks>
    IResponseSerializer? ProducerOf(string contentType);

    /// <summary>
    /// What writes a response nothing declared a content type for.
    /// </summary>
    /// <remarks>
    /// The serializer marked <see cref="IResponseSerializer.IsDefaultSerializer"/>, which is JSON
    /// unless an application replaced it. Null only where nothing registered one at all, which is a
    /// container with no serialization stack in it.
    /// </remarks>
    IResponseSerializer? DefaultSerializer { get; }
}
