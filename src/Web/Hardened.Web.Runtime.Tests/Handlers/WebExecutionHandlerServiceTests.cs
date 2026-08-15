using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Runtime.StaticContent;
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
    /// A matched route hands the request to the handler's own chain, and the static content and
    /// not-found paths are never reached.
    /// </summary>
    [Fact]
    public async Task AMatchedRouteRunsTheHandlersChain() {
        var fixture = new Fixture();
        var handlerChain = fixture.RouteMatches("/orders");

        await fixture.Service().Execute(fixture.Chain);

        await handlerChain.Received(1).Next();
        await fixture.StaticContent.DidNotReceive().Handle(Arg.Any<IExecutionContext>());
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
    /// No route, but a static file at that path: the request is served from disk and never becomes
    /// a 404.
    /// </summary>
    [Fact]
    public async Task ARequestWithNoRouteFallsBackToStaticContent() {
        var fixture = new Fixture();

        fixture.StaticContent.Handle(fixture.Context).Returns(true);

        await fixture.Service().Execute(fixture.Chain);

        await fixture.StaticContent.Received(1).Handle(fixture.Context);
        await fixture.NotFound.DidNotReceive().Handle(Arg.Any<IExecutionChain>());
    }

    /// <summary>
    /// No route and no static file is the only path to the not-found handler. Static content is
    /// tried first, so a request the file handler claims never reaches it.
    /// </summary>
    [Fact]
    public async Task ARequestWithNeitherARouteNorAFileReachesTheNotFoundHandler() {
        var fixture = new Fixture();

        fixture.StaticContent.Handle(fixture.Context).Returns(false);

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

        fixture.StaticContent.Handle(fixture.Context).Returns(false);

        await fixture.Service().Execute(fixture.Chain);

        fixture.RequestLogger.DidNotReceive().RequestMapped(Arg.Any<IExecutionContext>());
    }

    /// <summary>
    /// An application with no routing table at all — a web app that is only static content — still
    /// serves files rather than failing on the empty provider list.
    /// </summary>
    [Fact]
    public async Task AnApplicationWithNoProvidersStillServesStaticContent() {
        var fixture = new Fixture();

        fixture.StaticContent.Handle(fixture.Context).Returns(true);

        var service = new WebExecutionHandlerService(
            [], fixture.NotFound, fixture.MethodNotAllowed, fixture.RequestLogger, fixture.StaticContent);

        await service.Execute(fixture.Chain);

        await fixture.StaticContent.Received(1).Handle(fixture.Context);
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

        fixture.MethodMismatch("GET");
        fixture.StaticContent.Handle(fixture.Context).Returns(true);

        await fixture.Service().Execute(fixture.Chain);

        await fixture.MethodNotAllowed.DidNotReceive()
            .Handle(Arg.Any<IExecutionContext>(), Arg.Any<string>());
    }

    private sealed class Fixture {
        private readonly List<IWebExecutionRequestHandlerProvider> _providers = [];

        public Fixture() {
            Context = Substitute.For<IExecutionContext>();
            Context.Request.Returns(Substitute.For<IExecutionRequest>());
            Context.Response.Returns(Substitute.For<IExecutionResponse>());

            Chain = Substitute.For<IExecutionChain>();
            Chain.Context.Returns(Context);

            StaticContent = Substitute.For<IStaticContentHandler>();
            NotFound = Substitute.For<IResourceNotFoundHandler>();
            RequestLogger = Substitute.For<IRequestLogger>();
            HandlerInfo = Substitute.For<IExecutionRequestHandlerInfo>();
        }

        public IExecutionContext Context { get; }

        public IExecutionChain Chain { get; }

        public IStaticContentHandler StaticContent { get; }

        public IResourceNotFoundHandler NotFound { get; }

        public IRequestLogger RequestLogger { get; }

        public IExecutionRequestHandlerInfo HandlerInfo { get; }

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

        /// <summary>A provider that recognises the path but not the verb.</summary>
        public IWebExecutionRequestHandlerProvider MethodMismatch(string allow) {
            var provider = Substitute.For<IWebExecutionRequestHandlerProvider>();

            provider.GetExecutionRequestHandler(Arg.Any<IExecutionContext>())
                .Returns(RequestHandlerInfo.MethodNotAllowed(allow));

            _providers.Add(provider);

            return provider;
        }

        public WebExecutionHandlerService Service(params IWebExecutionRequestHandlerProvider[] providers) =>
            new(providers.Length > 0 ? providers : _providers,
                NotFound,
                MethodNotAllowed,
                RequestLogger,
                StaticContent);
    }
}
