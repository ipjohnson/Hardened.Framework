using System.Text;
using System.Text.Json;
using Amazon.Lambda.DynamoDBEvents;
using Hardened.Aws.Lambda.DynamoDb;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.DynamoDb.SUT.Tests;

/// <summary>
/// The image attributes and the epoch converter, driven directly rather than through a delivery.
/// </summary>
/// <remarks>
/// <para>
/// ChangeTests covers the path a real record takes, which only ever exercises the cases a healthy
/// stream produces. What is left is the refusals: a binding used outside [Change], an image the
/// record does not carry, and a parameter typed as something the attribute does not bind. Each one
/// throws or answers null by design, and the design is only worth having if it is what happens.
/// </para>
/// </remarks>
public class ImageBindingTests
{
    private static DynamoDbChange ChangeWith(
        Dictionary<string, DynamoDBEvent.AttributeValue>? newImage = null,
        Dictionary<string, DynamoDBEvent.AttributeValue>? oldImage = null
    ) =>
        new(
            "POST",
            "/",
            new MemoryStream(),
            new Dictionary<string, StringValues>(),
            new DynamoDBEvent.DynamodbStreamRecord
            {
                Dynamodb = new DynamoDBEvent.StreamRecord
                {
                    NewImage = newImage,
                    OldImage = oldImage,
                },
            }
        );

    private static IExecutionContext ContextFor(IExecutionRequest request)
    {
        var context = Substitute.For<IExecutionContext>();
        context.Request.Returns(request);
        return context;
    }

    [Fact]
    public async Task NewImage_BindsTheRowAsItIsNow()
    {
        var image = new Dictionary<string, DynamoDBEvent.AttributeValue>
        {
            ["Id"] = new() { S = "a-1" },
        };

        var bound = await new NewImageAttribute().BindValue<
            IDictionary<string, DynamoDBEvent.AttributeValue>
        >(ContextFor(ChangeWith(newImage: image)), Substitute.For<IExecutionRequestParameter>());

        Assert.Same(image, bound);
    }

    [Fact]
    public async Task OldImage_BindsTheRowAsItWas()
    {
        var image = new Dictionary<string, DynamoDBEvent.AttributeValue>
        {
            ["Id"] = new() { S = "a-0" },
        };

        var bound = await new OldImageAttribute().BindValue<
            IDictionary<string, DynamoDBEvent.AttributeValue>
        >(ContextFor(ChangeWith(oldImage: image)), Substitute.For<IExecutionRequestParameter>());

        Assert.Same(image, bound);
    }

    /// <summary>
    /// A REMOVE has no new image, and an INSERT has no old one. Both are legitimate answers rather
    /// than failures, so a nullable parameter receives null.
    /// </summary>
    [Fact]
    public async Task AnAbsentImage_IsNullRatherThanAThrow()
    {
        var bound = await new NewImageAttribute().BindValue<IDictionary<
            string,
            DynamoDBEvent.AttributeValue
        >?>(ContextFor(ChangeWith()), Substitute.For<IExecutionRequestParameter>());

        Assert.Null(bound);
    }

    [Fact]
    public async Task BoundOutsideAChange_SaysSo()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new NewImageAttribute().BindValue<
                IDictionary<string, DynamoDBEvent.AttributeValue>
            >(
                ContextFor(Substitute.For<IExecutionRequest>()),
                Substitute.For<IExecutionRequestParameter>()
            )
        );

        Assert.Contains("[NewImage]", exception.Message);
        Assert.Contains("[Change]", exception.Message);
    }

    /// <summary>
    /// A non-nullable parameter of the wrong type cannot be answered with null, so it is told what
    /// the attribute actually binds rather than receiving a default.
    /// </summary>
    [Fact]
    public async Task ANonNullableParameterOfTheWrongType_NamesWhatIsBound()
    {
        var exception = await Assert.ThrowsAsync<InvalidCastException>(async () =>
            await new OldImageAttribute().BindValue<int>(
                ContextFor(ChangeWith()),
                Substitute.For<IExecutionRequestParameter>()
            )
        );

        Assert.Contains("[OldImage]", exception.Message);
        Assert.Contains(nameof(Int32), exception.Message);
    }
}
