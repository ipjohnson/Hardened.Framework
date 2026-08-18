using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Runtime.Configuration;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Handlers;

/// <summary>
/// The step that turns a request into a handler.
///
/// <para>
/// Every generated routing table registers itself as an
/// <see cref="IWebExecutionRequestHandlerProvider"/>, and this service asks each of them in turn.
/// It owns three decisions that are invisible until one of them is wrong: which provider wins when
/// more than one matches, what the context is told about the match, and what happens when nothing
/// matches at all.
/// </para>
/// </summary>
public class WebExecutionHandlerServiceTests {

    /// <summary>
    /// A matched route hands the request to the handler's own chain, and neither the static content
    /// mount behind it nor the not-found path is reached.
    /// </summary>
    [Fact]
    public async Task AMatchedRouteRunsTheHandlersChain() {
        var fixture = new Fixture();
        var staticChain = fixture.StaticMount();
        var handlerChain = fixture.RouteMatches("/orders");

        await fixture.Service().Execute(fixture.Chain);

        await handlerChain.Received(1).Next();
        await staticChain.DidNotReceive().Next();
        await fixture.NotFound.DidNotReceive().Handle(Arg.Any<IExecutionChain>());
    }

    /// <summary>
    /// The context is told what matched before the chain runs: the path tokens the route bound, and
    /// the handler's own metadata. A filter reading <c>HandlerInfo</c> is downstream of this, so
    /// leaving either unset makes every filter see the previous request's route.
    /// </summary>
    [Fact]
    public async Task AMatchedRoutePutsItsTokensAndHandlerInfoOnTheContext() {
        var fixture = new Fixture();
        var tokens = new PathTokenCollection(1, ["id"]);

        fixture.RouteMatches("/orders/7", tokens);

        await fixture.Service().Execute(fixture.Chain);

        fixture.Context.Request.Received(1).PathTokens = tokens;
        fixture.Context.Received(1).HandlerInfo = fixture.HandlerInfo;
    }

    /// <summary>The match is logged as a mapped request, which is what ties a log line to a route.</summary>
    [Fact]
    public async Task AMatchedRouteIsLoggedAsMapped() {
        var fixture = new Fixture();

        fixture.RouteMatches("/orders");

        await fixture.Service().Execute(fixture.Chain);

        fixture.RequestLogger.Received(1).RequestMapped(fixture.Context);
    }

    /// <summary>
    /// Providers are consulted in reverse registration order, so the last one registered answers
    /// first. That is what lets an application override a route a referenced web library declared —
    /// reverse the order and the library always wins.
    /// </summary>
    [Fact]
    public async Task TheLastRegisteredProviderIsAskedFirst() {
        var fixture = new Fixture();

        var first = fixture.Provider(matches: true, "FirstRegistered");
        var second = fixture.Provider(matches: true, "LastRegistered");

        var service = fixture.Service(first, second);

        await service.Execute(fixture.Chain);

        // The winner is the one whose handler info reached the context.
        fixture.Context.Received(1).HandlerInfo =
            Arg.Is<IExecutionRequestHandlerInfo>(info => info.InvokeMethod == "LastRegistered");
    }

    /// <summary>A provider that does not match is passed over rather than ending the search.</summary>
    [Fact]
    public async Task AProviderThatDoesNotMatchFallsThroughToTheNextOne() {
        var fixture = new Fixture();

        var matching = fixture.Provider(matches: true, "Matching");
        var notMatching = fixture.Provider(matches: false, "NotMatching");

        // notMatching is registered last, so it is asked first and declines.
        var service = fixture.Service(matching, notMatching);

        await service.Execute(fixture.Chain);

        fixture.Context.Received(1).HandlerInfo =
            Arg.Is<IExecutionRequestHandlerInfo>(info => info.InvokeMethod == "Matching");
    }

    /// <summary>
    /// No route, but a static file at that path: the mount answers and the request never becomes a
    /// 404. The mount is consulted last, so this is what "nothing else claimed it" looks like.
    /// </summary>
    [Fact]
    public async Task ARequestWithNoRouteIsAnsweredByTheStaticMount() {
        var fixture = new Fixture();
        var staticChain = fixture.StaticMount();

        await fixture.Service().Execute(fixture.Chain);

        await staticChain.Received(1).Next();
        await fixture.NotFound.DidNotReceive().Handle(Arg.Any<IExecutionChain>());
    }

    /// <summary>
    /// A fallback is consulted after every ordinary provider, whatever order they were registered
    /// in. This is the guarantee that replaced "registered first, so consulted last" - which was a
    /// property of the registration site, and stopped being controllable once the provider shipped
    /// in its own package and an application chose where its module went.
    /// </summary>
    [Fact]
    public async Task AFallbackIsConsultedAfterEveryOrdinaryProvider() {
        var fixture = new Fixture();

        // Registered after the route, so reverse order alone would consult it first.
        var routeChain = fixture.RouteMatches("/orders");
        var staticChain = fixture.StaticMount();

        await fixture.Service().Execute(fixture.Chain);

        await routeChain.Received(1).Next();
        await staticChain.DidNotReceive().Next();
    }

    /// <summary>
    /// A static file runs a handler chain, which is the whole point of the mount being a provider:
    /// everything that hangs off a handler - authorization above all - is in that chain, and a
    /// direct call could never have any of it.
    /// </summary>
    [Fact]
    public async Task AStaticFileIsDispatchedLikeAnyOtherHandler() {
        var fixture = new Fixture();

        fixture.StaticMount();

        await fixture.Service().Execute(fixture.Chain);

        fixture.Context.Received(1).HandlerInfo = fixture.HandlerInfo;
        fixture.RequestLogger.Received(1).RequestMapped(fixture.Context);
    }

    /// <summary>
    /// No route and no static file is the only path to the not-found handler. The mount declines by
    /// having no provider answer, which is what a path with no file behind it looks like.
    /// </summary>
    [Fact]
    public async Task ARequestWithNeitherARouteNorAFileReachesTheNotFoundHandler() {
        var fixture = new Fixture();

        await fixture.Service().Execute(fixture.Chain);

        await fixture.NotFound.Received(1).Handle(fixture.Chain);
    }

    /// <summary>
    /// An unmatched request is not logged as mapped. The mapped log line means "this request found
    /// a route", and a static file or a 404 did not.
    /// </summary>
    [Fact]
    public async Task AnUnmatchedRequestIsNotLoggedAsMapped() {
        var fixture = new Fixture();

        await fixture.Service().Execute(fixture.Chain);

        fixture.RequestLogger.DidNotReceive().RequestMapped(Arg.Any<IExecutionContext>());
    }

    /// <summary>
    /// An application with no providers at all answers 404 rather than failing on the empty list.
    /// It used to still serve static content here, because static content was a call rather than a
    /// provider; an application with no providers now genuinely has no static content either.
    /// </summary>
    [Fact]
    public async Task AnApplicationWithNoProvidersAnswersNotFound() {
        var fixture = new Fixture();

        var service = new WebExecutionHandlerService(
            [], [], fixture.NotFound, fixture.MethodNotAllowed, fixture.RequestLogger,
            Options.Create<IWebRoutingConfiguration>(new WebRoutingConfiguration()));

        await service.Execute(fixture.Chain);

        await fixture.NotFound.Received(1).Handle(fixture.Chain);
    }

    /// <summary>
    /// A path a table recognised under another verb is a resource that exists. Answering 404 there
    /// made a real resource indistinguishable from a URL nobody declared - and API Gateway,
    /// CloudFront and generated clients all read the two differently.
    /// </summary>
    [Fact]
    public async Task APathMatchedUnderAnotherVerbIs405RatherThan404() {
        var fixture = new Fixture();

        fixture.MethodMismatch("GET, HEAD");

        await fixture.Service().Execute(fixture.Chain);

        await fixture.MethodNotAllowed.Received(1).Handle(fixture.Context, "GET, HEAD");
        await fixture.NotFound.DidNotReceive().Handle(Arg.Any<IExecutionChain>());
    }

    /// <summary>
    /// A later provider that does answer the verb wins. Providers are consulted in turn, so a 405
    /// from the first that merely recognised the path would shadow a table that had the route.
    /// </summary>
    [Fact]
    public async Task AProviderThatAnswersTheVerbBeatsOneThatOnlyMatchedThePath() {
        var fixture = new Fixture();

        fixture.MethodMismatch("GET");
        fixture.RouteMatches("/thing");

        await fixture.Service().Execute(fixture.Chain);

        await fixture.MethodNotAllowed.DidNotReceive()
            .Handle(Arg.Any<IExecutionContext>(), Arg.Any<string>());
    }

    /// <summary>
    /// Two tables declaring the same path under different verbs report both. Reporting one would
    /// tell a client the other verb is unavailable when it is not.
    /// </summary>
    [Fact]
    public async Task AllowedVerbsFromEveryTableAreReported() {
        var fixture = new Fixture();

        fixture.MethodMismatch("GET");
        fixture.MethodMismatch("POST");

        await fixture.Service().Execute(fixture.Chain);

        await fixture.MethodNotAllowed.Received(1)
            .Handle(fixture.Context, Arg.Is<string>(allow => allow.Contains("GET") && allow.Contains("POST")));
    }

    /// <summary>
    /// Static content still wins. A file at that path is a resource that answers the verb, and
    /// a 405 ahead of it would hide it.
    /// </summary>
    [Fact]
    public async Task StaticContentIsTriedBeforeThe405() {
        var fixture = new Fixture();
        var staticChain = fixture.StaticMount();

        fixture.MethodMismatch("GET");

        await fixture.Service().Execute(fixture.Chain);

        await staticChain.Received(1).Next();

        await fixture.MethodNotAllowed.DidNotReceive()
            .Handle(Arg.Any<IExecutionContext>(), Arg.Any<string>());
    }

    /// <summary>
    /// Strict is the default and what every existing application already behaves as: /orders and
    /// /orders/ are unrelated routes, which is also what an OpenAPI document says.
    /// </summary>
    [Fact]
    public async Task StrictLeavesTheOtherSpellingUnmatched() {
        var fixture = new Fixture();

        fixture.Context.Request.Path.Returns("/orders/");
        fixture.RouteMatchesPath("/orders");

        await fixture.Service().Execute(fixture.Chain);

        await fixture.NotFound.Received(1).Handle(fixture.Chain);
    }

    /// <summary>
    /// Normalise reaches the route, and the client sees no difference - which is what most
    /// applications want from a link somebody typed.
    /// </summary>
    [Fact]
    public async Task NormaliseReachesTheRouteWithoutTheSlash() {
        var fixture = new Fixture();

        fixture.Routing.TrailingSlash = TrailingSlash.Normalise;
        fixture.Context.Request.Path.Returns("/orders/");

        var chain = fixture.RouteMatchesPath("/orders");

        await fixture.Service().Execute(fixture.Chain);

        await chain.Received(1).Next();
        await fixture.NotFound.DidNotReceive().Handle(Arg.Any<IExecutionChain>());
    }

    /// <summary>And in the other direction, for a route declared with the slash.</summary>
    [Fact]
    public async Task NormaliseAlsoAddsAMissingSlash() {
        var fixture = new Fixture();

        fixture.Routing.TrailingSlash = TrailingSlash.Normalise;
        fixture.Context.Request.Path.Returns("/orders");

        var chain = fixture.RouteMatchesPath("/orders/");

        await fixture.Service().Execute(fixture.Chain);

        await chain.Received(1).Next();
    }

    /// <summary>
    /// Redirect answers 308 rather than 301: a redirect must not change the method, and most
    /// clients rewrite a 301 on a POST to GET, which silently drops the body.
    /// </summary>
    [Fact]
    public async Task RedirectAnswers308AtTheDeclaredPath() {
        var fixture = new Fixture();

        fixture.Routing.TrailingSlash = TrailingSlash.Redirect;
        fixture.Context.Request.Path.Returns("/orders/");
        fixture.RouteMatchesPath("/orders");

        await fixture.Service().Execute(fixture.Chain);

        fixture.Context.Response.Received().Status = 308;
        Assert.Equal("/orders", fixture.Headers["Location"].ToString());
    }

    /// <summary>
    /// And nothing is redirected to where nothing answers. A path no spelling matches is still a
    /// 404, not a redirect to another 404.
    /// </summary>
    [Fact]
    public async Task RedirectDoesNothingWhenNeitherSpellingMatches() {
        var fixture = new Fixture();

        fixture.Routing.TrailingSlash = TrailingSlash.Redirect;
        fixture.Context.Request.Path.Returns("/nothing/");

        await fixture.Service().Execute(fixture.Chain);

        await fixture.NotFound.Received(1).Handle(fixture.Chain);
    }

    /// <summary>
    /// The root has no other spelling, so it is left alone rather than redirected to the empty
    /// path.
    /// </summary>
    [Fact]
    public async Task TheRootIsNotRewritten() {
        var fixture = new Fixture();

        fixture.Routing.TrailingSlash = TrailingSlash.Redirect;
        fixture.Context.Request.Path.Returns("/");

        await fixture.Service().Execute(fixture.Chain);

        await fixture.NotFound.Received(1).Handle(fixture.Chain);
    }

    /// <summary>
    /// A table that recognised the path but named no verbs adds nothing to the header. It still
    /// counts as recognising the path — the request is a 405 rather than a 404 — but an empty
    /// contribution merged in as though it were a verb would produce a header with a stray comma,
    /// which a client parses as a verb whose name is the empty string.
    /// </summary>
    [Fact]
    public async Task ATableThatNamesNoVerbsAddsNothingToTheAllowHeader() {
        var fixture = new Fixture();

        fixture.Context.Request.Path.Returns("/thing");

        fixture.MethodMismatch("GET");
        fixture.MethodMismatch("");

        await fixture.Service().Execute(fixture.Chain);

        await fixture.MethodNotAllowed.Received(1).Handle(fixture.Context, "GET");
    }

    /// <summary>
    /// A HEAD reaches the GET handler and runs it in full, so the response carries the headers the
    /// GET would have carried. What differs is that the bytes are counted and dropped: the real
    /// body is put back untouched, and Content-Length reports what was measured.
    /// </summary>
    [Fact]
    public async Task AHeadRequestRunsTheHandlerAndReportsTheLengthOfTheBodyItDiscards() {
        var fixture = new Fixture();
        var realBody = new MemoryStream();

        fixture.Context.Request.Method.Returns("HEAD");
        fixture.Context.Response.Body = realBody;

        var handlerChain = fixture.RouteMatches("/orders");

        handlerChain.Next().Returns(_ => {
            fixture.Context.Response.Body.Write(new byte[10], 0, 10);

            return Task.CompletedTask;
        });

        await fixture.Service().Execute(fixture.Chain);

        Assert.Equal("10", fixture.Headers["Content-Length"]);
        Assert.Equal(0, realBody.Length);
        Assert.Same(realBody, fixture.Context.Response.Body);
    }

    /// <summary>
    /// The stream swapped in for the body counts every write path the framework has — the
    /// serializers, the raw output helper, the gzip wrapper and the newline the async-enumerable
    /// filter writes between items all end at <c>Response.Body</c>. A single overload left
    /// uncounted is a Content-Length that is quietly short.
    /// </summary>
    [Fact]
    public async Task TheDiscardingStreamCountsEveryWriteOverloadAndRefusesToBeRead() {
        var fixture = new Fixture();

        fixture.Context.Request.Method.Returns("HEAD");
        fixture.Context.Response.Body = new MemoryStream();

        var handlerChain = fixture.RouteMatches("/orders");

        Stream? discard = null;

        handlerChain.Next().Returns(_ => {
            discard = fixture.Context.Response.Body;

            discard.Write(new byte[3], 0, 3);
            discard.WriteByte(1);
            discard.Write(new byte[5].AsSpan());
            discard.WriteAsync(new byte[7], 0, 7).GetAwaiter().GetResult();
            discard.WriteAsync(new ReadOnlyMemory<byte>(new byte[11])).GetAwaiter().GetResult();

            discard.Flush();
            discard.FlushAsync().GetAwaiter().GetResult();

            return Task.CompletedTask;
        });

        await fixture.Service().Execute(fixture.Chain);

        Assert.Equal("27", fixture.Headers["Content-Length"]);

        // Write-only, and what it has counted is readable as the length.
        Assert.False(discard!.CanRead);
        Assert.False(discard.CanSeek);
        Assert.True(discard.CanWrite);
        Assert.Equal(27, discard.Length);
        Assert.Equal(27, discard.Position);

        // Nothing is kept, so every read or seek is a mistake rather than an empty answer.
        Assert.Throws<NotSupportedException>(() => discard.Position = 0);
        Assert.Throws<NotSupportedException>(() => discard.Read(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => discard.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => discard.SetLength(1));
    }

    /// <summary>
    /// A filter may start the response deliberately, and rewriting headers after that throws on
    /// Kestrel. The length is dropped rather than the request, because a HEAD that 500s is worse
    /// than one that answers without a Content-Length.
    /// </summary>
    [Fact]
    public async Task AHeadRequestLeavesContentLengthAloneOnceTheResponseHasStarted() {
        var fixture = new Fixture();

        fixture.Context.Request.Method.Returns("HEAD");
        fixture.Context.Response.Body = new MemoryStream();
        fixture.Context.Response.ResponseStarted.Returns(true);

        var handlerChain = fixture.RouteMatches("/orders");

        handlerChain.Next().Returns(_ => {
            fixture.Context.Response.Body.WriteByte(1);

            return Task.CompletedTask;
        });

        await fixture.Service().Execute(fixture.Chain);

        Assert.False(fixture.Headers.ContainsKey("Content-Length"));
    }

    /// <summary>
    /// The body is restored in a <c>finally</c>, so a handler that throws does not leave the
    /// counting stream in place for whatever writes the error response.
    /// </summary>
    [Fact]
    public async Task AHeadRequestPutsTheBodyBackWhenTheHandlerThrows() {
        var fixture = new Fixture();
        var realBody = new MemoryStream();

        fixture.Context.Request.Method.Returns("HEAD");
        fixture.Context.Response.Body = realBody;

        var handlerChain = fixture.RouteMatches("/orders");

        handlerChain.Next().Returns<Task>(_ => throw new InvalidOperationException("handler failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service().Execute(fixture.Chain));

        Assert.Same(realBody, fixture.Context.Response.Body);
    }

    /// <summary>
    /// The verb is compared without regard to case, because the method a client sends is not
    /// guaranteed to arrive uppercased and a lowercase <c>head</c> that fell through would send a
    /// body.
    /// </summary>
    [Fact]
    public async Task AHeadRequestIsRecognisedWhateverCaseItArrivesIn() {
        var fixture = new Fixture();

        fixture.Context.Request.Method.Returns("head");
        fixture.Context.Response.Body = new MemoryStream();

        var handlerChain = fixture.RouteMatches("/orders");

        handlerChain.Next().Returns(_ => {
            fixture.Context.Response.Body.WriteByte(1);

            return Task.CompletedTask;
        });

        await fixture.Service().Execute(fixture.Chain);

        Assert.Equal("1", fixture.Headers["Content-Length"]);
    }

    /// <summary>
    /// A GET is left alone: its bytes go to the real body and no length is invented for it.
    /// </summary>
    [Fact]
    public async Task AGetRequestWritesToTheRealBody() {
        var fixture = new Fixture();
        var realBody = new MemoryStream();

        fixture.Context.Request.Method.Returns("GET");
        fixture.Context.Response.Body = realBody;

        var handlerChain = fixture.RouteMatches("/orders");

        handlerChain.Next().Returns(_ => {
            fixture.Context.Response.Body.Write(new byte[4], 0, 4);

            return Task.CompletedTask;
        });

        await fixture.Service().Execute(fixture.Chain);

        Assert.Equal(4, realBody.Length);
        Assert.False(fixture.Headers.ContainsKey("Content-Length"));
    }

    private sealed class Fixture {
        private readonly List<IWebExecutionRequestHandlerProvider> _providers = [];

        public Fixture() {
            Context = Substitute.For<IExecutionContext>();
            Context.Request.Returns(Substitute.For<IExecutionRequest>());
            Context.Response.Returns(Substitute.For<IExecutionResponse>());
            Context.Response.Headers.Returns(Headers);

            // The trailing-slash probe asks the tables about the other spelling, which it does by
            // cloning the request onto a cloned context rather than mutating the one in flight.
            // Both clones have to behave, or the probe reads the original path back and every
            // policy looks like strict.
            Context.Request.Clone(
                    Arg.Any<string?>(),
                    Arg.Any<string?>(),
                    Arg.Any<IDictionary<string, StringValues>?>(),
                    Arg.Any<IQueryStringCollection?>(),
                    Arg.Any<IReadOnlyList<string>?>())
                .Returns(call => {
                    var request = Substitute.For<IExecutionRequest>();

                    request.Path.Returns((string?)call[1]);

                    return request;
                });

            Context.Clone(
                    Arg.Any<IExecutionRequest?>(),
                    Arg.Any<IExecutionResponse?>(),
                    Arg.Any<IServiceProvider?>(),
                    Arg.Any<IMetricLogger?>())
                .Returns(call => {
                    var cloned = Substitute.For<IExecutionContext>();

                    cloned.Request.Returns((IExecutionRequest?)call[0]);
                    cloned.Response.Returns(Context.Response);

                    return cloned;
                });

            Chain = Substitute.For<IExecutionChain>();
            Chain.Context.Returns(Context);

            NotFound = Substitute.For<IResourceNotFoundHandler>();
            RequestLogger = Substitute.For<IRequestLogger>();
            HandlerInfo = Substitute.For<IExecutionRequestHandlerInfo>();
        }

        public IExecutionContext Context { get; }

        public IExecutionChain Chain { get; }

        public IResourceNotFoundHandler NotFound { get; }

        public IRequestLogger RequestLogger { get; }

        public IExecutionRequestHandlerInfo HandlerInfo { get; }

        /// <summary>The response headers, so a test can read what was written to them.</summary>
        public Dictionary<string, StringValues> Headers { get; } = new();

        /// <summary>
        /// A provider that answers only for one exact path, whichever context it is asked about.
        /// </summary>
        /// <remarks>
        /// <see cref="RouteMatches"/> answers for the context it was given, which is the wrong
        /// shape for the trailing-slash probe: that asks about a clone, and a provider keyed on the
        /// original would answer for both spellings and prove nothing.
        /// </remarks>
        public IExecutionChain RouteMatchesPath(string path) {
            var handlerChain = Substitute.For<IExecutionChain>();
            var handler = Substitute.For<IExecutionRequestHandler>();

            handler.HandlerInfo.Returns(HandlerInfo);
            handler.GetExecutionChain(Arg.Any<IExecutionContext>()).Returns(handlerChain);

            var provider = Substitute.For<IWebExecutionRequestHandlerProvider>();

            provider.GetExecutionRequestHandler(Arg.Any<IExecutionContext>())
                .Returns(call =>
                    ((IExecutionContext)call[0]).Request.Path == path
                        ? new RequestHandlerInfo(handler, PathTokenCollection.Empty)
                        : null);

            _providers.Add(provider);

            return handlerChain;
        }

        /// <summary>Registers a provider that matches, and returns the chain its handler hands back.</summary>
        public IExecutionChain RouteMatches(string path, PathTokenCollection? tokens = null) {
            Context.Request.Path.Returns(path);

            var handlerChain = Substitute.For<IExecutionChain>();
            var handler = Substitute.For<IExecutionRequestHandler>();

            handler.HandlerInfo.Returns(HandlerInfo);
            handler.GetExecutionChain(Context).Returns(handlerChain);

            var provider = Substitute.For<IWebExecutionRequestHandlerProvider>();

            provider.GetExecutionRequestHandler(Context)
                .Returns(new RequestHandlerInfo(handler, tokens ?? PathTokenCollection.Empty));

            _providers.Add(provider);

            return handlerChain;
        }

        /// <summary>A provider identified by the invoke method name its handler reports.</summary>
        public IWebExecutionRequestHandlerProvider Provider(bool matches, string invokeMethod) {
            var provider = Substitute.For<IWebExecutionRequestHandlerProvider>();

            if (!matches) {
                provider.GetExecutionRequestHandler(Arg.Any<IExecutionContext>())
                    .Returns((RequestHandlerInfo?)null);

                return provider;
            }

            var handlerInfo = Substitute.For<IExecutionRequestHandlerInfo>();
            handlerInfo.InvokeMethod.Returns(invokeMethod);

            var handler = Substitute.For<IExecutionRequestHandler>();
            handler.HandlerInfo.Returns(handlerInfo);
            handler.GetExecutionChain(Arg.Any<IExecutionContext>()).Returns(Substitute.For<IExecutionChain>());

            provider.GetExecutionRequestHandler(Arg.Any<IExecutionContext>())
                .Returns(new RequestHandlerInfo(handler, PathTokenCollection.Empty));

            return provider;
        }

        public IMethodNotAllowedHandler MethodNotAllowed { get; } =
            Substitute.For<IMethodNotAllowedHandler>();

        /// <summary>
        /// A fallback provider, shaped like the static content mount: answering whatever nothing
        /// else claimed.
        /// </summary>
        /// <remarks>
        /// Registered as a fallback rather than at the front of the ordinary list, because that is
        /// what decides when it is consulted now. Ordering used to be a property of where the
        /// registration sat, and a provider shipping in its own package cannot control that.
        /// </remarks>
        public IExecutionChain StaticMount() {
            var handlerChain = Substitute.For<IExecutionChain>();
            var handler = Substitute.For<IExecutionRequestHandler>();

            handler.HandlerInfo.Returns(HandlerInfo);
            handler.GetExecutionChain(Arg.Any<IExecutionContext>()).Returns(handlerChain);

            var provider = Substitute.For<IFallbackRequestHandlerProvider>();

            provider.GetExecutionRequestHandler(Arg.Any<IExecutionContext>())
                .Returns(new RequestHandlerInfo(handler, PathTokenCollection.Empty));

            _fallbacks.Add(provider);

            return handlerChain;
        }

        /// <summary>A provider that recognises the path but not the verb.</summary>
        public IWebExecutionRequestHandlerProvider MethodMismatch(string allow) {
            var provider = Substitute.For<IWebExecutionRequestHandlerProvider>();

            provider.GetExecutionRequestHandler(Arg.Any<IExecutionContext>())
                .Returns(RequestHandlerInfo.MethodNotAllowed(allow));

            _providers.Add(provider);

            return provider;
        }

        /// <summary>How the pipeline treats a path no route matched exactly.</summary>
        public WebRoutingConfiguration Routing { get; } = new();

        private readonly List<IFallbackRequestHandlerProvider> _fallbacks = new();

        public WebExecutionHandlerService Service(params IWebExecutionRequestHandlerProvider[] providers) =>
            new(providers.Length > 0 ? providers : _providers,
                _fallbacks,
                NotFound,
                MethodNotAllowed,
                RequestLogger,
                Options.Create<IWebRoutingConfiguration>(Routing));
    }
}
