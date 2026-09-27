using System.Collections.Generic;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.SourceGenerator.Requests;
using Xunit;

namespace Hardened.SourceGenerator.Tests.SpecBridge;

/// <summary>
/// How the framework's record, thrown, becomes the body a contract declares at its status: the
/// dictionary a described operation's handler info carries.
/// </summary>
/// <remarks>
/// The 0.41 trial's C-20: under throws mode, <c>throw new NotFound(...).AsException()</c> sent the
/// framework's problem document where the model declared <c>VanNotFound</c>. Asserted on the
/// model's expression here. The OpenAPI and Smithy fixtures compile it and throw through it.
/// </remarks>
public class DeclaredErrorConversionsTests
{
    private const string Dictionary =
        "new global::System.Collections.Generic.Dictionary<int, global::System.Func<object, object?>> { ";

    private static SchemaModel Van()
    {
        var schema = new SchemaModel { Name = "Van", Kind = SchemaKind.Object };

        schema.Properties.Add(
            new PropertyModel
            {
                Name = "vin",
                Type = "string",
                IsRequired = true,
            }
        );
        schema.Required.Add("vin");

        return schema;
    }

    private static SchemaModel Problem()
    {
        var schema = new SchemaModel { Name = "Problem", Kind = SchemaKind.Object };

        schema.Properties.Add(new PropertyModel { Name = "type", Type = "string" });
        schema.Properties.Add(new PropertyModel { Name = "title", Type = "string" });
        schema.Properties.Add(new PropertyModel { Name = "status", Type = "integer" });
        schema.Properties.Add(new PropertyModel { Name = "detail", Type = "string" });

        return schema;
    }

    /// <summary>A Smithy error shape: <c>@error</c> and a required message.</summary>
    private static SchemaModel ErrorShape(string name)
    {
        var schema = new SchemaModel
        {
            Name = name,
            Kind = SchemaKind.Object,
            IsErrorShape = true,
        };

        schema.Properties.Add(
            new PropertyModel
            {
                Name = "message",
                Type = "string",
                IsRequired = true,
            }
        );
        schema.Required.Add("message");

        return schema;
    }

    private static SchemaModel Plain(string name)
    {
        var schema = new SchemaModel { Name = name, Kind = SchemaKind.Object };

        schema.Properties.Add(
            new PropertyModel
            {
                Name = "code",
                Type = "string",
                IsRequired = true,
            }
        );
        schema.Required.Add("code");

        return schema;
    }

    private static ErrorResponseModel Error(int status, string schema) =>
        new() { StatusCode = status, Ref = "#/components/schemas/" + schema };

    /// <summary>An error the build generates a type for, as the allocator names a Smithy one.</summary>
    private static ErrorResponseModel Named(int status, string schema) =>
        new()
        {
            StatusCode = status,
            Ref = "#/components/schemas/" + schema,
            Name = schema,
            TypeName = schema + "Error",
            ExceptionTypeName = schema + "Exception",
        };

    private static string? Conversions(
        IReadOnlyList<SchemaModel> schemas,
        params ErrorResponseModel[] errors
    )
    {
        var operation = new OperationModel
        {
            OperationId = "getVan",
            Path = "/vans/{vin}",
            HttpMethod = "GET",
            ResponseRef = "#/components/schemas/Van",
            SuccessStatusCode = 200,
            SuccessResponses =
            {
                new SuccessResponseModel { StatusCode = 200, Ref = "#/components/schemas/Van" },
            },
            Parameters = new List<ParameterModel>
            {
                new()
                {
                    Name = "vin",
                    In = "path",
                    IsRequired = true,
                    Type = "string",
                },
            },
        };

        operation.ErrorResponses.AddRange(errors);

        var spec = new ServiceSpecModel
        {
            FileName = "fleet",
            ResponseModel = SpecResponseModel.Throws,
            Schemas = [Van(), .. schemas],
            Services = new List<ServiceModel>
            {
                new()
                {
                    Tag = "Van",
                    Operations = new List<OperationModel> { operation },
                },
            },
        };

        return SpecHandlerModelBuilder
            .BuildModels(
                spec,
                "Fleet.Models",
                "Fleet.Services",
                "Fleet.Generated",
                "Fleet.Validation"
            )
            .Single()
            .ResponseInformation.DeclaredErrorConversionsExpression;
    }

    /// <summary>
    /// A Smithy error shape takes the record's detail as its message, or the title where the
    /// record has no detail.
    /// </summary>
    [Fact]
    public void AnErrorShapeIsBuiltFromTheRecordsDetail()
    {
        Assert.Equal(
            Dictionary
                + "{ 404, value => value is global::Hardened.Web.Runtime.Responses.NotFound record"
                + " ? new global::Fleet.Models.VanNotFound((record.Detail ?? record.Title)) : null } }",
            Conversions([ErrorShape("VanNotFound")], Named(404, "VanNotFound"))
        );
    }

    /// <summary>A problem body is built from the record's four members.</summary>
    [Fact]
    public void AProblemIsBuiltFromTheRecordsMembers()
    {
        Assert.Equal(
            Dictionary
                + "{ 404, value => value is global::Hardened.Web.Runtime.Responses.NotFound record"
                + " ? new global::Fleet.Models.Problem(record.Type, record.Title, record.Status, record.Detail)"
                + " : null } }",
            Conversions([Problem()], Error(404, "Problem"))
        );
    }

    /// <summary>One entry per status, in status order.</summary>
    [Fact]
    public void EachConvertibleStatusHasAnEntry()
    {
        var conversions = Conversions(
            [Problem(), ErrorShape("VanTaken")],
            Named(409, "VanTaken"),
            Error(404, "Problem")
        );

        Assert.NotNull(conversions);
        Assert.True(
            conversions.IndexOf("{ 404,", System.StringComparison.Ordinal)
                < conversions.IndexOf("{ 409,", System.StringComparison.Ordinal)
        );
        Assert.Contains("global::Hardened.Web.Runtime.Responses.Conflict record", conversions);
    }

    /// <summary>
    /// Two errors at one status leave a thrown record meaning neither, so the status converts
    /// nothing.
    /// </summary>
    [Fact]
    public void TwoErrorsAtOneStatusConvertNeither()
    {
        Assert.Null(
            Conversions(
                [ErrorShape("VanNotFound"), ErrorShape("FleetNotFound")],
                Named(404, "VanNotFound"),
                Named(404, "FleetNotFound")
            )
        );
    }

    /// <summary>A body with a required member no record holds is not built from one.</summary>
    [Fact]
    public void ABodyARecordCannotFillHasNoConversion()
    {
        Assert.Null(Conversions([Plain("ApiError")], Error(404, "ApiError")));
    }
}
