using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// An enum with no attribute of its own, which is the case the default has to be right for.
/// </summary>
public enum Priority { Low, InProgress, OnHold }

/// <summary>
/// The opt-out, for an enum whose member names are already its wire values.
/// </summary>
[JsonEnumNaming(EnumNaming.MemberName)]
public enum LegacyCode { AB12, CD34 }

/// <summary>
/// A vocabulary chosen for the API rather than for C#.
/// </summary>
[JsonEnumNaming(EnumNaming.KebabCaseLower)]
public enum Shipping { NextDay, TwoDay }

public record Ticket(string Title, Priority Priority);

public record Order(LegacyCode Code, Shipping Shipping);

/// <summary>
/// An enum reaching the wire by all three routes it can take.
/// </summary>
/// <remarks>
/// A body written, a body read and a query parameter bound, because they are served by different
/// code and have disagreed before: the body resolves through the type-info chain and a parameter is
/// text the binder converts, so an enum can be configured for one and not the other. The published
/// document is asserted from the same values in
/// <c>Hardened.IntegrationTests.WebApp.SUT.Tests.EnumVocabularyTests</c>.
/// </remarks>
[BasePath("/enum-vocabulary")]
public class EnumVocabularyController {

    [Get("/ticket")]
    public Ticket DefaultNaming() => new("Ship it", Priority.InProgress);

    [Post("/ticket")]
    public Priority ReadDefaultNaming(Ticket ticket) => ticket.Priority;

    [Get("/order")]
    public Order DeclaredNaming() => new(LegacyCode.AB12, Shipping.NextDay);

    /// <summary>
    /// The route that never reaches a JSON converter. <c>?priority=inProgress</c> has to bind to
    /// the same value the body carries under that name.
    /// </summary>
    [Get("/by-priority")]
    public string FromQuery([FromQueryString] Priority priority) => priority.ToString();

    [Get("/by-shipping")]
    public string DeclaredNamingFromQuery([FromQueryString] Shipping shipping) =>
        shipping.ToString();
}
