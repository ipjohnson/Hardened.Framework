using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;

namespace Hardened.Azure.Functions.SourceGenerator;

/// <summary>One parameter of a generated shim, before the <c>FunctionContext</c> every shim ends with.</summary>
internal sealed class ShimParameter {
    public ShimParameter(ITypeDefinition type, string name, bool isTrigger) {
        Type = type;
        Name = name;
        IsTrigger = isTrigger;
    }

    public ITypeDefinition Type { get; }

    public string Name { get; }

    /// <summary>Whether this is the parameter the binding attribute is written on.</summary>
    public bool IsTrigger { get; }
}

/// <summary>
/// A fixed-delay retry policy as the module wrote it: the count and the delay, each as the C#
/// text the shim's attribute re-emits and as the value the provider's metadata carries.
/// </summary>
internal sealed class RetryPolicy {
    public RetryPolicy(string countText, string count, string delayText, string delay) {
        CountText = countText;
        Count = count;
        DelayText = delayText;
        Delay = delay;
    }

    public string CountText { get; }

    public string Count { get; }

    public string DelayText { get; }

    public string Delay { get; }
}

/// <summary>The binding attribute's arguments, as C# text.</summary>
internal sealed class AttributeArguments {
    public AttributeArguments(IReadOnlyList<string> positional, IReadOnlyList<KeyValuePair<string, string>> named) {
        Positional = positional;
        Named = named;
    }

    public IReadOnlyList<string> Positional { get; }

    public IReadOnlyList<KeyValuePair<string, string>> Named { get; }
}

/// <summary>
/// How one family of neutral trigger becomes a worker function: the attribute the shim carries,
/// what the worker binds for it, and the bindings the host is told about.
/// </summary>
/// <remarks>
/// <para>
/// This table is the one place the Azure line names an Azure binding for a neutral trigger, and it
/// is here rather than in the adapter packages because a generator can see no assembly but its
/// own. Each row is written against one adapter package: what the shim binds is what that
/// adapter's <c>ITriggerAdapter.Handles</c> recognises, and the module whose settings it reads is
/// the one that package's targets bind.
/// </para>
/// <para>
/// <b>The raw bindings have to agree with the Worker SDK's build task.</b> The task scans the
/// compiled shim and writes <c>functions.metadata</c> from the same attribute; the host indexes
/// whichever description it is given, and every Azure fixture asserts the two are equal. So the
/// JSON below is the task's own shape: the parameter name, the direction, the type the attribute's
/// name lowers to, <c>dataType</c> for a string parameter the extension does not bind itself, the
/// attribute's constructor arguments under their parameter names, its named arguments in camel
/// case, <c>cardinality</c> in place of <c>IsBatched</c>, and <c>supportsDeferredBinding</c> where
/// the extension's converter advertises the parameter type.
/// </para>
/// <para>
/// <b>Per source or per family.</b> A queue, a topic, a timer, a stream, a change feed and a blob
/// container each get a function of their own, because the host binds a function to one entity
/// and the function's identity is the route. Event Grid and HTTP get one function each for the
/// whole family: an Event Grid subscription and an HTTP route prefix deliver everything, and
/// which handler runs is decided from the event or the request in the pipeline.
/// </para>
/// </remarks>
internal abstract class AzureBinding {
    protected const string Worker = "Microsoft.Azure.Functions.Worker";

    /// <summary>The scheme the handler routes under, which is what selects a row.</summary>
    public abstract string Scheme { get; }

    /// <summary>The build property naming the module that serves this family.</summary>
    public abstract string Property { get; }

    /// <summary>
    /// The first half of a per-source function name - <c>Queue</c> in <c>Queue_orders</c> - or the
    /// whole name of a per-family one.
    /// </summary>
    public abstract string FunctionPrefix { get; }

    /// <summary>Whether every handler gets a function, or the family shares one.</summary>
    public abstract bool PerSource { get; }

    /// <summary>The worker's binding attribute, written on the trigger parameter.</summary>
    public abstract ITypeDefinition Attribute { get; }

    /// <summary>The shim's parameters before the <c>FunctionContext</c>, the trigger first.</summary>
    public abstract IReadOnlyList<ShimParameter> Parameters { get; }

    /// <summary>What the shim returns to the host, or null for a function that answers nothing.</summary>
    public virtual ITypeDefinition? ReturnType => null;

    /// <summary>Which dispatch the invocation runs through: the trigger table, or the web one.</summary>
    public virtual string Dispatch => "Trigger";

    /// <summary>The module settings a function of this family cannot be written without.</summary>
    public virtual IReadOnlyList<string> RequiredSettings => Array.Empty<string>();

    /// <summary>The module settings this family reads, required or not, for HRDAZ004.</summary>
    public virtual IReadOnlyList<string> ReadSettings => RequiredSettings;

    /// <summary>
    /// The settings a function of this family cannot be written without and the module did not
    /// supply, for HRDAZ003: the required ones, and for the families with a retry policy, the
    /// half of the pair the application left out.
    /// </summary>
    public virtual IReadOnlyList<string> MissingSettings(ModuleSettings settings) {
        var missing = new List<string>();

        foreach (var required in RequiredSettings) {
            if (settings.Get(required) == null) {
                missing.Add(required);
            }
        }

        return missing;
    }

    /// <summary>
    /// The retry policy the shim carries and the metadata declares, or null where the family has
    /// none or the module wrote none.
    /// </summary>
    public virtual RetryPolicy? Retry(ModuleSettings settings) => null;

    public abstract AttributeArguments Arguments(string source, ModuleSettings settings);

    /// <summary>The bindings as the host is told them, in the build task's shape.</summary>
    public abstract IReadOnlyList<string> RawBindings(string source, ModuleSettings settings);

    /// <summary>
    /// The expression the shim hands the invocation handler: the trigger parameter, unless the
    /// family binds more than one thing and has to bundle them.
    /// </summary>
    public virtual string DataExpression => Parameters[0].Name;

    public ShimParameter Trigger => Parameters[0];

    /// <summary>Every trigger this generator can write a function for.</summary>
    public static readonly IReadOnlyList<AzureBinding> All = new AzureBinding[] {
        new QueueBinding(),
        new TopicBinding(),
        new TimerBinding(),
        new StreamBinding(),
        new ChangeBinding(),
        new BlobBinding(),
        new EventBinding(),
        new HttpBinding()
    };

    public static AzureBinding? For(string scheme) {
        foreach (var binding in All) {
            if (binding.Scheme == scheme) {
                return binding;
            }
        }

        return null;
    }

    /// <summary>The Service Bus module's settings, shared by the queue and topic rows.</summary>
    private static void ServiceBusArguments(
        List<KeyValuePair<string, string>> named, ModuleSettings settings) {
        named.Add(new KeyValuePair<string, string>("IsBatched", "true"));

        if (settings.Get("Connection") is { } connection) {
            named.Add(new KeyValuePair<string, string>("Connection", connection.Text));
        }

        // The host completes a batch itself unless told not to; settling per message means
        // telling it not to, and the adapter completing what it accepted.
        if (settings.Get("ReportsItemFailures")?.Flag == true) {
            named.Add(new KeyValuePair<string, string>("AutoCompleteMessages", "false"));
        }
    }

    private static void ServiceBusJson(JsonObject json, ModuleSettings settings) {
        if (settings.Get("Connection")?.Literal is { } connection) {
            json.String("connection", connection);
        }

        if (settings.Get("ReportsItemFailures")?.Flag == true) {
            json.Bool("autoCompleteMessages", false);
        }

        json.String("cardinality", "Many");
        json.Raw("properties", "{\"supportsDeferredBinding\":\"True\"}");
    }

    /// <summary>
    /// The retry policy the stream and change feed modules carry, as <c>RetryCount</c> and
    /// <c>RetryDelay</c>, which the host applies as a fixed delay between attempts.
    /// </summary>
    /// <remarks>
    /// Both or neither. The host reads the count and the delay as one policy, and the two families
    /// that carry it are the ones whose source does not deliver a failed batch again by itself, so
    /// half a policy is a function whose failures nothing retries, written by an application that
    /// asked for retries. <see cref="RetryMissing"/> names the missing half for HRDAZ003.
    /// </remarks>
    private static RetryPolicy? FixedDelayRetry(ModuleSettings settings) {
        var count = settings.Get("RetryCount");
        var delay = settings.Get("RetryDelay");

        if (count?.Number == null || delay?.Literal == null) {
            return null;
        }

        return new RetryPolicy(count.Text, count.Number, delay.Text, delay.Literal);
    }

    private static void RetryMissing(List<string> missing, ModuleSettings settings) {
        var count = settings.Get("RetryCount") != null;
        var delay = settings.Get("RetryDelay") != null;

        if (count && !delay) {
            missing.Add("RetryDelay");
        }
        else if (delay && !count) {
            missing.Add("RetryCount");
        }
    }

    private static readonly IReadOnlyList<ShimParameter> ServiceBusParameters = new[] {
        new ShimParameter(
            TypeDefinition.Get("Azure.Messaging.ServiceBus", "ServiceBusReceivedMessage", isArray: true),
            "messages", isTrigger: true),
        new ShimParameter(TypeDefinition.Get(Worker, "ServiceBusMessageActions"), "messageActions", isTrigger: false)
    };

    private const string ServiceBusDelivery =
        "new global::Hardened.Azure.Functions.ServiceBus.ServiceBusDelivery(messages, messageActions)";

    /// <summary>
    /// <c>[Queue("orders")]</c>: a batched Service Bus trigger on the queue, bound as
    /// <c>ServiceBusReceivedMessage[]</c> with the settlement actions beside it, for
    /// <c>Hardened.Azure.Functions.ServiceBus</c>.
    /// </summary>
    private sealed class QueueBinding : AzureBinding {
        public override string Scheme => "QUEUE";
        public override string Property => "HardenedQueueModule";
        public override string FunctionPrefix => "Queue";
        public override bool PerSource => true;
        public override ITypeDefinition Attribute { get; } = TypeDefinition.Get(Worker, "ServiceBusTriggerAttribute");
        public override IReadOnlyList<ShimParameter> Parameters => ServiceBusParameters;
        public override IReadOnlyList<string> ReadSettings { get; } = new[] { "Connection", "ReportsItemFailures" };
        public override string DataExpression => ServiceBusDelivery;

        public override AttributeArguments Arguments(string source, ModuleSettings settings) {
            var named = new List<KeyValuePair<string, string>>();

            ServiceBusArguments(named, settings);

            return new AttributeArguments(new[] { QuoteString(source) }, named);
        }

        public override IReadOnlyList<string> RawBindings(string source, ModuleSettings settings) {
            var json = new JsonObject()
                .String("name", "messages")
                .String("direction", "In")
                .String("type", "serviceBusTrigger")
                .String("queueName", source);

            ServiceBusJson(json, settings);

            return new[] { json.ToString() };
        }
    }

    /// <summary>
    /// <c>[Topic("order-events")]</c>: the same trigger on a subscription of the topic. The
    /// subscription is the module's, because it is named for the consumer.
    /// </summary>
    private sealed class TopicBinding : AzureBinding {
        public override string Scheme => "TOPIC";
        public override string Property => "HardenedTopicModule";
        public override string FunctionPrefix => "Topic";
        public override bool PerSource => true;
        public override ITypeDefinition Attribute { get; } = TypeDefinition.Get(Worker, "ServiceBusTriggerAttribute");
        public override IReadOnlyList<ShimParameter> Parameters => ServiceBusParameters;
        public override IReadOnlyList<string> RequiredSettings { get; } = new[] { "Subscription" };
        public override IReadOnlyList<string> ReadSettings { get; } = new[] { "Subscription", "Connection", "ReportsItemFailures" };
        public override string DataExpression => ServiceBusDelivery;

        public override AttributeArguments Arguments(string source, ModuleSettings settings) {
            var named = new List<KeyValuePair<string, string>>();

            ServiceBusArguments(named, settings);

            return new AttributeArguments(
                new[] { QuoteString(source), settings.Get("Subscription")!.Text }, named);
        }

        public override IReadOnlyList<string> RawBindings(string source, ModuleSettings settings) {
            var json = new JsonObject()
                .String("name", "messages")
                .String("direction", "In")
                .String("type", "serviceBusTrigger")
                .String("topicName", source)
                .String("subscriptionName", settings.Get("Subscription")!.Literal!);

            ServiceBusJson(json, settings);

            return new[] { json.ToString() };
        }
    }

    /// <summary>
    /// <c>[Timer("nightly")]</c>: a timer whose schedule is the app setting named after the
    /// trigger, so the expression stays a deployment setting as the attribute promises.
    /// </summary>
    private sealed class TimerBinding : AzureBinding {
        public override string Scheme => "TIMER";
        public override string Property => "HardenedTimerModule";
        public override string FunctionPrefix => "Timer";
        public override bool PerSource => true;
        public override ITypeDefinition Attribute { get; } = TypeDefinition.Get(Worker, "TimerTriggerAttribute");

        public override IReadOnlyList<ShimParameter> Parameters { get; } = new[] {
            new ShimParameter(TypeDefinition.Get(typeof(string)), "timer", isTrigger: true)
        };

        private static string Schedule(string source) => "%Hardened:Timers:" + source + "%";

        public override AttributeArguments Arguments(string source, ModuleSettings settings) =>
            new(new[] { QuoteString(Schedule(source)) }, Array.Empty<KeyValuePair<string, string>>());

        public override IReadOnlyList<string> RawBindings(string source, ModuleSettings settings) =>
            new[] {
                new JsonObject()
                    .String("name", "timer")
                    .String("direction", "In")
                    .String("type", "timerTrigger")
                    .String("dataType", "String")
                    .String("schedule", Schedule(source))
                    .Raw("properties", "{}")
                    .ToString()
            };
    }

    /// <summary>
    /// <c>[Stream("orders")]</c>: a batched Event Hubs trigger, bound as <c>EventData[]</c> for
    /// <c>Hardened.Azure.Functions.EventHubs</c>.
    /// </summary>
    /// <remarks>
    /// The connection is always written, because the Event Hubs extension has no default of its
    /// own: a trigger that names none fails the host at startup with "No event hub receiver
    /// named ...". <see cref="DefaultConnection"/> follows the Service Bus extension's naming, so
    /// a deployment sets <c>AzureWebJobsEventHubs</c> beside <c>AzureWebJobsServiceBus</c>.
    /// </remarks>
    private sealed class StreamBinding : AzureBinding {
        public const string DefaultConnection = "AzureWebJobsEventHubs";

        public override string Scheme => "STREAM";
        public override string Property => "HardenedStreamModule";
        public override string FunctionPrefix => "Stream";
        public override bool PerSource => true;
        public override ITypeDefinition Attribute { get; } = TypeDefinition.Get(Worker, "EventHubTriggerAttribute");
        public override IReadOnlyList<string> ReadSettings { get; } = new[] { "Connection", "ConsumerGroup", "RetryCount", "RetryDelay" };

        public override IReadOnlyList<string> MissingSettings(ModuleSettings settings) {
            var missing = (List<string>)base.MissingSettings(settings);

            RetryMissing(missing, settings);

            return missing;
        }

        public override RetryPolicy? Retry(ModuleSettings settings) => FixedDelayRetry(settings);

        public override IReadOnlyList<ShimParameter> Parameters { get; } = new[] {
            new ShimParameter(
                TypeDefinition.Get("Azure.Messaging.EventHubs", "EventData", isArray: true), "events", isTrigger: true)
        };

        public override AttributeArguments Arguments(string source, ModuleSettings settings) {
            var named = new List<KeyValuePair<string, string>> {
                new("IsBatched", "true"),
                new("Connection", settings.Get("Connection")?.Text ?? QuoteString(DefaultConnection))
            };

            if (settings.Get("ConsumerGroup") is { } group) {
                named.Add(new KeyValuePair<string, string>("ConsumerGroup", group.Text));
            }

            return new AttributeArguments(new[] { QuoteString(source) }, named);
        }

        public override IReadOnlyList<string> RawBindings(string source, ModuleSettings settings) {
            var json = new JsonObject()
                .String("name", "events")
                .String("direction", "In")
                .String("type", "eventHubTrigger")
                .String("eventHubName", source)
                .String("connection", settings.Get("Connection")?.Literal ?? DefaultConnection);

            if (settings.Get("ConsumerGroup")?.Literal is { } group) {
                json.String("consumerGroup", group);
            }

            json.String("cardinality", "Many");
            json.Raw("properties", "{\"supportsDeferredBinding\":\"True\"}");

            return new[] { json.ToString() };
        }
    }

    /// <summary>
    /// <c>[Change("orders")]</c>: the Cosmos change feed of one container, bound as the JSON the
    /// host sends so the adapter can split it, for <c>Hardened.Azure.Functions.CosmosDb</c>. The
    /// database and the lease container are the module's.
    /// </summary>
    private sealed class ChangeBinding : AzureBinding {
        public override string Scheme => "CHANGE";
        public override string Property => "HardenedChangeModule";
        public override string FunctionPrefix => "Change";
        public override bool PerSource => true;
        public override ITypeDefinition Attribute { get; } = TypeDefinition.Get(Worker, "CosmosDBTriggerAttribute");
        public override IReadOnlyList<string> RequiredSettings { get; } = new[] { "Database" };
        public override IReadOnlyList<string> ReadSettings { get; } = new[] { "Database", "Connection", "LeaseContainer", "RetryCount", "RetryDelay" };

        public override IReadOnlyList<string> MissingSettings(ModuleSettings settings) {
            var missing = (List<string>)base.MissingSettings(settings);

            RetryMissing(missing, settings);

            return missing;
        }

        public override RetryPolicy? Retry(ModuleSettings settings) => FixedDelayRetry(settings);

        public override IReadOnlyList<ShimParameter> Parameters { get; } = new[] {
            new ShimParameter(TypeDefinition.Get(typeof(string)), "documents", isTrigger: true)
        };

        public override AttributeArguments Arguments(string source, ModuleSettings settings) {
            var named = new List<KeyValuePair<string, string>>();

            if (settings.Get("Connection") is { } connection) {
                named.Add(new KeyValuePair<string, string>("Connection", connection.Text));
            }

            if (settings.Get("LeaseContainer") is { } lease) {
                named.Add(new KeyValuePair<string, string>("LeaseContainerName", lease.Text));
            }

            // The lease container is the extension's own bookkeeping, and creating it is what
            // lets a deployment run against a fresh database.
            named.Add(new KeyValuePair<string, string>("CreateLeaseContainerIfNotExists", "true"));

            return new AttributeArguments(
                new[] { settings.Get("Database")!.Text, QuoteString(source) }, named);
        }

        public override IReadOnlyList<string> RawBindings(string source, ModuleSettings settings) {
            var json = new JsonObject()
                .String("name", "documents")
                .String("direction", "In")
                .String("type", "cosmosDBTrigger")
                .String("dataType", "String")
                .String("databaseName", settings.Get("Database")!.Literal!)
                .String("containerName", source);

            if (settings.Get("Connection")?.Literal is { } connection) {
                json.String("connection", connection);
            }

            if (settings.Get("LeaseContainer")?.Literal is { } lease) {
                json.String("leaseContainerName", lease);
            }

            json.Bool("createLeaseContainerIfNotExists", true);
            json.Raw("properties", "{}");

            return new[] { json.ToString() };
        }
    }

    /// <summary>
    /// <c>[Blob("uploads")]</c>: a blob trigger on the container, fed by Event Grid, bound as a
    /// <c>BlobClient</c> so nothing is downloaded, for <c>Hardened.Azure.Functions.Blobs</c>.
    /// </summary>
    private sealed class BlobBinding : AzureBinding {
        public override string Scheme => "BLOB";
        public override string Property => "HardenedBlobModule";
        public override string FunctionPrefix => "Blob";
        public override bool PerSource => true;
        public override ITypeDefinition Attribute { get; } = TypeDefinition.Get(Worker, "BlobTriggerAttribute");
        public override IReadOnlyList<string> ReadSettings { get; } = new[] { "Connection" };

        public override IReadOnlyList<ShimParameter> Parameters { get; } = new[] {
            new ShimParameter(TypeDefinition.Get("Azure.Storage.Blobs", "BlobClient"), "blob", isTrigger: true)
        };

        private static string Path(string source) => source + "/{name}";

        public override AttributeArguments Arguments(string source, ModuleSettings settings) {
            var named = new List<KeyValuePair<string, string>>();

            if (settings.Get("Connection") is { } connection) {
                named.Add(new KeyValuePair<string, string>("Connection", connection.Text));
            }

            named.Add(new KeyValuePair<string, string>(
                "Source", "global::" + Worker + ".BlobTriggerSource.EventGrid"));

            return new AttributeArguments(new[] { QuoteString(Path(source)) }, named);
        }

        public override IReadOnlyList<string> RawBindings(string source, ModuleSettings settings) {
            var json = new JsonObject()
                .String("name", "blob")
                .String("direction", "In")
                .String("type", "blobTrigger")
                .String("path", Path(source));

            if (settings.Get("Connection")?.Literal is { } connection) {
                json.String("connection", connection);
            }

            json.String("source", "EventGrid");
            json.Raw("properties", "{\"supportsDeferredBinding\":\"True\"}");

            return new[] { json.ToString() };
        }
    }

    /// <summary>
    /// <c>[Event(source, type)]</c>, all of them: one Event Grid function for the family, bound as
    /// the CloudEvent's JSON for <c>Hardened.Azure.Functions.EventGrid</c> to parse and route.
    /// </summary>
    private sealed class EventBinding : AzureBinding {
        public override string Scheme => "EVENT";
        public override string Property => "HardenedEventModule";
        public override string FunctionPrefix => "Event";
        public override bool PerSource => false;
        public override ITypeDefinition Attribute { get; } = TypeDefinition.Get(Worker, "EventGridTriggerAttribute");

        public override IReadOnlyList<ShimParameter> Parameters { get; } = new[] {
            new ShimParameter(TypeDefinition.Get(typeof(string)), "cloudEvent", isTrigger: true)
        };

        public override AttributeArguments Arguments(string source, ModuleSettings settings) =>
            new(Array.Empty<string>(), Array.Empty<KeyValuePair<string, string>>());

        public override IReadOnlyList<string> RawBindings(string source, ModuleSettings settings) =>
            new[] {
                new JsonObject()
                    .String("name", "cloudEvent")
                    .String("direction", "In")
                    .String("type", "eventGridTrigger")
                    .String("dataType", "String")
                    .String("cardinality", "One")
                    .Raw("properties", "{}")
                    .ToString()
            };
    }

    /// <summary>
    /// The web verbs, all of them: one anonymous HTTP function catching every method under every
    /// path, for <c>Hardened.Azure.Functions.Http</c>, whose routing table does the routing.
    /// </summary>
    private sealed class HttpBinding : AzureBinding {
        private static readonly string[] Methods = { "get", "post", "put", "patch", "delete", "head", "options" };

        public override string Scheme => "HTTP";
        public override string Property => "HardenedHttpModule";
        public override string FunctionPrefix => "Http";
        public override bool PerSource => false;
        public override ITypeDefinition Attribute { get; } = TypeDefinition.Get(Worker, "HttpTriggerAttribute");
        public override ITypeDefinition ReturnType { get; } = TypeDefinition.Get(Worker + ".Http", "HttpResponseData");
        public override string Dispatch => "Web";

        public override IReadOnlyList<ShimParameter> Parameters { get; } = new[] {
            new ShimParameter(TypeDefinition.Get(Worker + ".Http", "HttpRequestData"), "request", isTrigger: true)
        };

        public override AttributeArguments Arguments(string source, ModuleSettings settings) {
            var positional = new List<string> { "global::" + Worker + ".AuthorizationLevel.Anonymous" };

            foreach (var method in Methods) {
                positional.Add(QuoteString(method));
            }

            return new AttributeArguments(
                positional,
                new[] { new KeyValuePair<string, string>("Route", QuoteString("{*path}")) });
        }

        public override IReadOnlyList<string> RawBindings(string source, ModuleSettings settings) =>
            new[] {
                new JsonObject()
                    .String("name", "request")
                    .String("direction", "In")
                    .String("type", "httpTrigger")
                    .String("authLevel", "Anonymous")
                    .Strings("methods", Methods)
                    .String("route", "{*path}")
                    .Raw("properties", "{}")
                    .ToString(),
                // The return binding the build task adds for a function returning HttpResponseData.
                new JsonObject()
                    .String("name", "$return")
                    .String("type", "http")
                    .String("direction", "Out")
                    .ToString()
            };
    }

    /// <summary>A JSON object written in insertion order, which is the build task's order too.</summary>
    internal sealed class JsonObject {
        private readonly StringBuilder _builder = new StringBuilder("{");
        private bool _first = true;

        private JsonObject Key(string key) {
            if (!_first) {
                _builder.Append(',');
            }

            _first = false;
            _builder.Append('"').Append(Escape(key)).Append("\":");

            return this;
        }

        public JsonObject String(string key, string value) {
            Key(key)._builder.Append('"').Append(Escape(value)).Append('"');

            return this;
        }

        public JsonObject Bool(string key, bool value) {
            Key(key)._builder.Append(value ? "true" : "false");

            return this;
        }

        public JsonObject Raw(string key, string json) {
            Key(key)._builder.Append(json);

            return this;
        }

        public JsonObject Strings(string key, IEnumerable<string> values) {
            Key(key)._builder.Append('[');

            var first = true;

            foreach (var value in values) {
                if (!first) {
                    _builder.Append(',');
                }

                first = false;
                _builder.Append('"').Append(Escape(value)).Append('"');
            }

            _builder.Append(']');

            return this;
        }

        public override string ToString() => _builder + "}";

        /// <summary>
        /// The characters a JSON string cannot carry bare. No Azure entity name allows them, and the
        /// escape is here so a name from another source cannot break the metadata.
        /// </summary>
        internal static string Escape(string value) {
            var builder = new StringBuilder(value.Length);

            foreach (var character in value) {
                switch (character) {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < ' ') {
                            builder.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else {
                            builder.Append(character);
                        }

                        break;
                }
            }

            return builder.ToString();
        }
    }
}
