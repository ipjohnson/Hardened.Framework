namespace Hardened1.Tests;

/// <summary>
/// [HardenedTest] boots the real application and resolves the test's parameters from its
/// container, so what runs here is the same pipeline a deployed function runs - the adapter, the
/// binder, the filters and the handler - without a cloud.
/// </summary>
/// <remarks>
/// The façade is generated from the handler's own attribute. The method exists because the source
/// does, and the parameter type is the one the handler binds, so a renamed source or a changed
/// payload is a compile error here rather than a test that passes against nothing.
/// </remarks>
public class OrderHandlerTests {

#if (invoke)
    [HardenedTest]
    public async Task ThePayloadReachesTheHandler(Application.Invocations invocations, OrderLog log) {
        var accepted = await invocations.Process(new Order { Id = "A-1", Quantity = 2 });

#if (xunit)
        Assert.Equal("A-1", accepted.Id);
        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
#else
        Assert.That(accepted.Id, Is.EqualTo("A-1"));
        Assert.That(log.Orders.Select(order => order.Id), Is.EqualTo(new[] { "A-1" }));
#endif
    }

    /// <summary>The return value comes back to the caller rather than being discarded.</summary>
    [HardenedTest]
    public async Task TheHandlersReturnValueComesBack(Application.Invocations invocations) {
        var accepted = await invocations.Process(new Order { Id = "A-2", Quantity = 1 });

#if (xunit)
        Assert.Equal(1, accepted.Received);
#else
        Assert.That(accepted.Received, Is.EqualTo(1));
#endif
    }
#endif
#if (queue)
    [HardenedTest]
    public async Task AMessageReachesTheHandler(Application.Queues queues, OrderLog log) {
        await queues.Orders(new Order { Id = "A-1", Quantity = 2 });

#if (xunit)
        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
#else
        Assert.That(log.Orders.Select(order => order.Id), Is.EqualTo(new[] { "A-1" }));
#endif
    }

    /// <summary>
    /// One invocation, one call per message. The route was chosen once from the queue the batch
    /// arrived against; the fan-out is the framework's.
    /// </summary>
    [HardenedTest]
    public async Task EveryMessageInABatchIsHandled(Application.Queues queues, OrderLog log) {
        await queues.Orders(
            new Order { Id = "A-1" }, new Order { Id = "A-2" }, new Order { Id = "A-3" });

#if (xunit)
        Assert.Equal(3, log.Orders.Count);
#else
        Assert.That(log.Orders, Has.Count.EqualTo(3));
#endif
    }
#endif
#if (topic)
    [HardenedTest]
    public async Task ANotificationReachesTheHandler(Application.Topics topics, OrderLog log) {
        await topics.Orders(new Order { Id = "A-1", Quantity = 2 });

#if (xunit)
        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
#else
        Assert.That(log.Orders.Select(order => order.Id), Is.EqualTo(new[] { "A-1" }));
#endif
    }
#endif
#if (timer)
    [HardenedTest]
    public async Task TheScheduleReachesTheHandler(Application.Timers timers, OrderLog log) {
        await timers.Nightly();

#if (xunit)
        Assert.Equal(1, log.Sweeps);
#else
        Assert.That(log.Sweeps, Is.EqualTo(1));
#endif
    }
#endif
#if (change)
    [HardenedTest]
    public async Task AChangedRowReachesTheHandler(Application.Changes changes, OrderLog log) {
        await changes.Orders(new Order { Id = "A-1", Quantity = 2 });

#if (xunit)
        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
#else
        Assert.That(log.Orders.Select(order => order.Id), Is.EqualTo(new[] { "A-1" }));
#endif
    }
#endif
#if (stream)
    [HardenedTest]
    public async Task ARecordReachesTheHandler(Application.Streams streams, OrderLog log) {
        await streams.Orders(new Order { Id = "A-1", Quantity = 2 });

#if (xunit)
        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
#else
        Assert.That(log.Orders.Select(order => order.Id), Is.EqualTo(new[] { "A-1" }));
#endif
    }
#endif
#if (blob)
    [HardenedTest]
    public async Task ANotificationReachesTheHandler(Application.Blobs blobs, OrderLog log) {
        await blobs.Uploads(new Upload { Key = "report.pdf", Size = 1024 });

#if (xunit)
        Assert.Equal("report.pdf", Assert.Single(log.Uploads).Key);
#else
        Assert.That(log.Uploads.Select(upload => upload.Key), Is.EqualTo(new[] { "report.pdf" }));
#endif
    }
#endif
}
