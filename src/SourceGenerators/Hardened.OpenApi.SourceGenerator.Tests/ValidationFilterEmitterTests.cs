using Hardened.OpenApi.SourceGenerator.Emitters;
using Hardened.OpenApi.SourceGenerator.Models;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

public class ValidationFilterEmitterTests {
    [Fact]
    public void Emit_WithRequiredBodyProperty_ContainsRequiredRule() {
        var operation = new OperationModel {
            OperationId = "createPet",
            Path = "/pets",
            HttpMethod = "POST",
            Tag = "Pet",
            RequestBodyRef = "#/components/schemas/CreatePetRequest",
            RequestBodyProperties = new List<PropertyModel> {
                new() { Name = "name", IsRequired = true, Type = "string", MinLength = 1, MaxLength = 100 }
            },
            RequestBodyRequired = new List<string> { "name" }
        };

        var source = ValidationFilterEmitter.Emit(operation, "Test.Generated", "Test.Models");

        Assert.NotNull(source);
        Assert.Contains("CreatePet_ValidationFilterProvider", source);
        Assert.Contains("RequiredRule.Instance", source);
        Assert.Contains("new StringLengthRule(1, 100)", source);
        Assert.Contains("body => ((Test.Models.CreatePetRequest)body).Name", source);
        Assert.Contains("FilterOrder.Validation", source);
    }

    [Fact]
    public void Emit_WithParameterConstraints_ContainsRangeRule() {
        var operation = new OperationModel {
            OperationId = "listPets",
            Path = "/pets",
            HttpMethod = "GET",
            Tag = "Pet",
            Parameters = new List<ParameterModel> {
                new() {
                    Name = "limit",
                    In = "query",
                    IsRequired = false,
                    Type = "integer",
                    Minimum = 1m,
                    Maximum = 100m
                }
            }
        };

        var source = ValidationFilterEmitter.Emit(operation, "Test.Generated", "Test.Models");

        Assert.NotNull(source);
        Assert.Contains("new RangeRule(1m, 100m, false, false)", source);
        Assert.Contains("ParameterValidationInfo", source);
    }

    [Fact]
    public void Emit_WithPatternConstraint_ContainsPatternRule() {
        var operation = new OperationModel {
            OperationId = "searchPets",
            Path = "/pets/search",
            HttpMethod = "GET",
            Tag = "Pet",
            Parameters = new List<ParameterModel> {
                new() {
                    Name = "q",
                    In = "query",
                    IsRequired = true,
                    Type = "string",
                    Pattern = "^[a-zA-Z]+$"
                }
            }
        };

        var source = ValidationFilterEmitter.Emit(operation, "Test.Generated", "Test.Models");

        Assert.NotNull(source);
        Assert.Contains("RequiredRule.Instance", source);
        Assert.Contains("new PatternRule(\"^[a-zA-Z]+$\")", source);
    }

    [Fact]
    public void Emit_WithEnumConstraint_ContainsEnumRule() {
        var operation = new OperationModel {
            OperationId = "listPets",
            Path = "/pets",
            HttpMethod = "GET",
            Tag = "Pet",
            Parameters = new List<ParameterModel> {
                new() {
                    Name = "status",
                    In = "query",
                    IsRequired = false,
                    Type = "string",
                    EnumValues = new List<string> { "available", "pending", "sold" }
                }
            }
        };

        var source = ValidationFilterEmitter.Emit(operation, "Test.Generated", "Test.Models");

        Assert.NotNull(source);
        Assert.Contains("new EnumRule(new string[] { \"available\", \"pending\", \"sold\" })", source);
    }

    [Fact]
    public void Emit_NoConstraints_ReturnsNull() {
        var operation = new OperationModel {
            OperationId = "listPets",
            Path = "/pets",
            HttpMethod = "GET",
            Tag = "Pet",
            Parameters = new List<ParameterModel> {
                new() { Name = "limit", In = "query", IsRequired = false, Type = "integer" }
            }
        };

        var source = ValidationFilterEmitter.Emit(operation, "Test.Generated", "Test.Models");

        Assert.Null(source);
    }

    [Fact]
    public void Emit_ExclusiveRange_ContainsCorrectFlags() {
        var operation = new OperationModel {
            OperationId = "createItem",
            Path = "/items",
            HttpMethod = "POST",
            Tag = "Item",
            RequestBodyRef = "#/components/schemas/CreateItemRequest",
            RequestBodyProperties = new List<PropertyModel> {
                new() {
                    Name = "price",
                    Type = "number",
                    Minimum = 0m,
                    ExclusiveMinimum = true,
                    Maximum = 10000m
                }
            }
        };

        var source = ValidationFilterEmitter.Emit(operation, "Test.Generated", "Test.Models");

        Assert.NotNull(source);
        Assert.Contains("new RangeRule(0m, 10000m, true, false)", source);
    }
}
