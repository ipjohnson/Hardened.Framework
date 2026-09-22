using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// Every status a contract can declare as an error, bound to the response the framework ships
/// for it.
/// </summary>
/// <remarks>
/// <c>ShippedResponses</c> names a record per status, and a <c>Status&lt;Http.X&gt;</c> marker for a
/// status with no record of its own. A name that is not a type the runtime ships is a compile
/// error in the generated handler, so one operation declaring all of them, compiled, checks the
/// whole table.
/// </remarks>
public class DeclaredErrorBindingTests
{
    /// <summary>Statuses with a record of their own, which takes the declared body.</summary>
    private static readonly int[] Records =
    [
        400,
        401,
        402,
        403,
        404,
        405,
        408,
        409,
        410,
        412,
        413,
        415,
        422,
        428,
        429,
        500,
        501,
        502,
        503,
        504,
    ];

    /// <summary>Statuses the framework ships no record for, answered through a marker.</summary>
    private static readonly int[] Markers =
    [
        407,
        411,
        414,
        416,
        417,
        418,
        421,
        423,
        424,
        425,
        426,
        431,
        451,
        505,
        506,
        507,
        508,
        510,
        511,
    ];

    private static readonly Dictionary<string, string> ResponseModel = new()
    {
        ["HardenedResponseModel"] = "Response",
    };

    /// <summary>
    /// A contract with one operation declaring every status with a problem body, and one declaring
    /// every status with none.
    /// </summary>
    private static string Contract()
    {
        var yaml = new StringBuilder(
            """
            openapi: "3.0.0"
            info: { title: Refusals, version: "1.0" }
            paths:
              /with-body:
                get:
                  tags: [Refusal]
                  operationId: withBody
                  responses:
                    '200':
                      description: OK
                      content:
                        application/json:
                          schema: { type: string }

            """
        );

        foreach (var status in Records.Concat(Markers))
        {
            yaml.Append(
                $"""
                        '{status}':
                          description: Status {status}
                          content:
                            application/json:
                              schema:
                                $ref: '#/components/schemas/Problem'

                """
            );
        }

        yaml.Append(
            """
              /bodyless:
                get:
                  tags: [Refusal]
                  operationId: bodyless
                  responses:
                    '200':
                      description: OK
                      content:
                        application/json:
                          schema: { type: string }

            """
        );

        foreach (var status in Records.Concat(Markers).Append(304).Append(406))
        {
            yaml.Append(
                $"""
                        '{status}':
                          description: Status {status}

                """
            );
        }

        yaml.Append(
            """
            components:
              schemas:
                Problem:
                  type: object
                  properties:
                    type: { type: string }
                    title: { type: string }
                    status: { type: integer, format: int32 }
                    detail: { type: string }
            """
        );

        return yaml.ToString();
    }

    private static string Generated()
    {
        var result = OpenApiGenerator.Run(Contract(), buildProperties: ResponseModel);

        result.AssertNoErrors();

        return Regex.Replace(string.Concat(result.GeneratedSources.Values), @"\s+", "");
    }

    [Fact]
    public void AStatusWithARecordBindsToItWithTheDeclaredBody()
    {
        var generated = Generated();

        foreach (
            var record in new[]
            {
                "Unauthorized",
                "PaymentRequired",
                "MethodNotAllowed",
                "RequestTimeout",
                "Gone",
                "PreconditionFailed",
                "ContentTooLarge",
                "UnsupportedMediaType",
                "InternalServerError",
                "NotImplemented",
                "BadGateway",
                "GatewayTimeout",
            }
        )
        {
            Assert.Contains(
                $"global::Hardened.Web.Runtime.Responses.{record}<global::TestNamespace.Models.Problem>",
                generated
            );
        }
    }

    [Fact]
    public void AStatusWithNoRecordBindsThroughItsMarker()
    {
        var generated = Generated();

        foreach (
            var marker in new[]
            {
                "ProxyAuthenticationRequired",
                "LengthRequired",
                "UriTooLong",
                "RangeNotSatisfiable",
                "ExpectationFailed",
                "ImATeapot",
                "MisdirectedRequest",
                "Locked",
                "FailedDependency",
                "TooEarly",
                "UpgradeRequired",
                "RequestHeaderFieldsTooLarge",
                "UnavailableForLegalReasons",
                "HttpVersionNotSupported",
                "VariantAlsoNegotiates",
                "InsufficientStorage",
                "LoopDetected",
                "NotExtended",
                "NetworkAuthenticationRequired",
            }
        )
        {
            Assert.Contains(
                "global::Hardened.Web.Runtime.Responses.Status<global::Hardened.Web.Runtime.Responses.Http."
                    + marker
                    + ",global::TestNamespace.Models.Problem>",
                generated
            );
            Assert.Contains(
                "global::Hardened.Web.Runtime.Responses.Status<global::Hardened.Web.Runtime.Responses.Http."
                    + marker
                    + ">",
                generated
            );
        }
    }
}
