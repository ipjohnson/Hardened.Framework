using System.Collections.Generic;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.Idl.SourceGenerator;
using Hardened.SourceGenerator.Requests;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// What the specification-first handler model tells the dispatch about a declared response set.
/// </summary>
/// <remarks>
/// <para>
/// The half that was missing. Both directions share <c>InvokeMethodCodeGenerator</c>, which emits a
/// per-case switch when <c>UnionCases</c> is set and a plain assignment when it is not - and this
/// builder never set it. The interface returned a response set, the case types were emitted, and the
/// handler assigned the container whole: the wrapper went on the wire nested under its own Value, at
/// whatever status the operation would have answered anyway.
/// </para>
/// <para>
/// Asserted on the model rather than on generated text, because the text is
/// <c>InvokeMethodCodeGenerator</c>'s and is covered where it lives. What is worth pinning here is
/// that the cases reach it at all, and that each one carries the status and the body the contract
/// declared.
/// </para>
/// </remarks>
public class ResponseSetDispatchModelTests {

    private static ServiceSpecModel Spec(SpecResponseModel mode) =>
        new() {
            FileName = "todos",
            ResponseModel = mode,
            Services = new List<ServiceModel> {
                new() {
                    Tag = "Todo",
                    Operations = new List<OperationModel> {
                        new() {
                            OperationId = "getTodo",
                            Path = "/todos/{id}",
                            HttpMethod = "GET",
                            ResponseRef = "#/components/schemas/Todo",
                            SuccessStatusCode = 200,
                            SuccessResponses = {
                                new SuccessResponseModel {
                                    StatusCode = 200, Ref = "#/components/schemas/Todo"
                                }
                            },
                            ErrorResponses = {
                                new ErrorResponseModel {
                                    StatusCode = 404, Ref = "#/components/schemas/Problem"
                                }
                            },
                            Parameters = new List<ParameterModel> {
                                new() { Name = "id", In = "path", IsRequired = true, Type = "integer" }
                            }
                        },
                        new() {
                            OperationId = "removeTodo",
                            Path = "/todos/{id}",
                            HttpMethod = "DELETE",
                            SuccessStatusCode = 204,
                            SuccessResponses = { new SuccessResponseModel { StatusCode = 204 } },
                            ErrorResponses = {
                                new ErrorResponseModel {
                                    StatusCode = 404, Ref = "#/components/schemas/Problem"
                                }
                            },
                            Parameters = new List<ParameterModel> {
                                new() { Name = "id", In = "path", IsRequired = true, Type = "integer" }
                            }
                        }
                    }
                }
            }
        };

    private static IReadOnlyList<UnionCaseModel> Cases(SpecResponseModel mode, string operationId) {
        var model = RequestModelBuilder
            .BuildModels(Spec(mode), "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated",
                "Test.Api.Validation")
            .Single(m => m.HandlerMethod.Equals(operationId, System.StringComparison.OrdinalIgnoreCase));

        return UnionResponseSelector.Decode(model.ResponseInformation.UnionCases);
    }

    [Fact]
    public void Standard_CarriesNoCases() {
        var model = RequestModelBuilder
            .BuildModels(Spec(SpecResponseModel.Standard), "Test.Api.Models", "Test.Api.Services",
                "Test.Api.Generated", "Test.Api.Validation")
            .Single(m => m.HandlerMethod == "GetTodo");

        Assert.Null(model.ResponseInformation.UnionCases);
    }

    [Fact]
    public void Response_CarriesOneCasePerDeclaredStatus() {
        var cases = Cases(SpecResponseModel.Response, "GetTodo");

        Assert.Equal(new[] { 200, 404 }, cases.Select(c => c.Status));
    }

    /// <summary>
    /// The success is named by its own schema; the error is a generated wrapper whose Body is the
    /// payload. Sending the wrapper instead would ship that payload nested under a member no client
    /// was told about, which is exactly what the missing switch did.
    /// </summary>
    [Fact]
    public void Response_TheErrorCaseCarriesItsBodyRatherThanItself() {
        var cases = Cases(SpecResponseModel.Response, "GetTodo");

        var success = cases.Single(c => c.Status == 200);
        var error = cases.Single(c => c.Status == 404);

        Assert.Equal("global::Test.Api.Models.Todo", success.TypeName);
        Assert.False(success.CarriesBody);

        Assert.Equal("global::Test.Api.Models.GetTodoNotFound", error.TypeName);
        Assert.True(error.CarriesBody);
        Assert.Equal("global::Test.Api.Models.Problem", error.BodyTypeName);
    }

    /// <summary>
    /// A 204 is a case that serializes nothing. Without it the set held only the 404 and a handler
    /// had no way to say it had succeeded.
    /// </summary>
    [Fact]
    public void Response_ABodylessSuccessIsACaseThatSerializesNothing() {
        var cases = Cases(SpecResponseModel.Response, "RemoveTodo");

        var success = cases.Single(c => c.Status == 204);

        Assert.Equal("global::Test.Api.Models.RemoveTodoNoContent", success.TypeName);
        Assert.False(success.HasBody);
    }

    /// <summary>
    /// Union and Response differ in one emitted declaration and nothing else - the dispatch is the
    /// same switch over the same cases, which is what makes moving between them cost no handler.
    /// </summary>
    [Fact]
    public void Union_DispatchesTheSameCasesAsResponse() {
        Assert.Equal(
            Cases(SpecResponseModel.Response, "GetTodo").Select(c => (c.TypeName, c.Status)),
            Cases(SpecResponseModel.Union, "GetTodo").Select(c => (c.TypeName, c.Status)));
    }
}
