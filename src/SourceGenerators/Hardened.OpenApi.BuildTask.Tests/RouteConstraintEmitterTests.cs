using System.Collections.Generic;
using Hardened.Generation.Models;
using Hardened.Idl.Validation;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Which path patterns a description's routing table compiles in.
/// </summary>
/// <remarks>
/// The 0.41 trial's B-05 and C-03: a path value that failed its pattern answered a bodyless 404
/// under an operation whose document declares its 404 with a body. A pattern is now a validation
/// rule, answered with a 400 naming the parameter, except where two operations need their
/// constraints to be told apart.
/// </remarks>
public class RouteConstraintEmitterTests
{
    private static OperationModel Operation(
        string id,
        string method,
        string path,
        params ParameterModel[] parameters
    ) =>
        new()
        {
            OperationId = id,
            HttpMethod = method,
            Path = path,
            Parameters = new List<ParameterModel>(parameters),
        };

    private static ParameterModel PathParameter(string name, string? pattern) =>
        new()
        {
            Name = name,
            In = "path",
            Type = "string",
            IsRequired = true,
            Pattern = pattern,
        };

    private static string Emit(params OperationModel[] operations) =>
        EmitterHarness.Write(
            ns =>
                RouteConstraintEmitter.Emit(
                    ns,
                    new ServiceSpecModel
                    {
                        Services = new List<ServiceModel>
                        {
                            new() { Operations = new List<OperationModel>(operations) },
                        },
                    },
                    new PatternRegistry(EmitterHarness.RootNamespace + ".Validation", "vans")
                ),
            EmitterHarness.RootNamespace + ".Validation"
        );

    [Fact]
    public void APathPatternOnARouteOfItsOwnStaysAValidationRule()
    {
        var vin = PathParameter("vin", "^[A-HJ-NPR-Z0-9]{17}$");

        var output = Emit(Operation("getVan", "GET", "/vans/{vin}", vin));

        Assert.Null(vin.RouteConstraint);
        Assert.True(vin.HasValidationConstraints);
        Assert.DoesNotContain("SpecRouteConstraints", output);
    }

    /// <summary>
    /// Two operations on one shape match the same URLs without their constraints, so the
    /// constraints are what route a value to one or the other.
    /// </summary>
    [Fact]
    public void PatternsThatTellTwoRoutesApartStayInTheRoute()
    {
        var vin = PathParameter("vin", "^[A-HJ-NPR-Z0-9]{17}$");
        var code = PathParameter("fleetCode", "^[a-z]{3}$");

        var output = Emit(
            Operation("getVan", "GET", "/vans/{vin}", vin),
            Operation("getFleet", "GET", "/vans/{fleetCode}", code)
        );

        Assert.NotNull(vin.RouteConstraint);
        Assert.NotNull(code.RouteConstraint);
        Assert.NotEqual(vin.RouteConstraint, code.RouteConstraint);
        Assert.False(vin.HasValidationConstraints);
        Assert.Contains("class SpecRouteConstraints", output);
        Assert.Contains("\"" + vin.RouteConstraint + "\"", output);
        Assert.Contains("\"" + code.RouteConstraint + "\"", output);
    }

    /// <summary>
    /// Another verb on the same path is a different route, and needs no constraint to be told
    /// apart.
    /// </summary>
    [Fact]
    public void AnotherVerbOnTheSamePathDoesNotShareTheShape()
    {
        var read = PathParameter("vin", "^[A-HJ-NPR-Z0-9]{17}$");
        var write = PathParameter("vin", "^[A-HJ-NPR-Z0-9]{17}$");

        Emit(
            Operation("getVan", "GET", "/vans/{vin}", read),
            Operation("putVan", "PUT", "/vans/{vin}", write)
        );

        Assert.Null(read.RouteConstraint);
        Assert.Null(write.RouteConstraint);
    }

    /// <summary>A literal segment is a different shape from a token.</summary>
    [Fact]
    public void ALiteralSegmentIsNotATokensShape()
    {
        var vin = PathParameter("vin", "^[A-HJ-NPR-Z0-9]{17}$");

        Emit(
            Operation("getVan", "GET", "/vans/{vin}", vin),
            Operation("listVans", "GET", "/vans/all")
        );

        Assert.Null(vin.RouteConstraint);
    }
}
