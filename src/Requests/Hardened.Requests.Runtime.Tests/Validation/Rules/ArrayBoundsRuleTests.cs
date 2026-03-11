using Hardened.Requests.Abstract.Validation;
using Hardened.Requests.Runtime.Validation.Rules;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Validation.Rules;

public class ArrayBoundsRuleTests {
    [Fact]
    public void BelowMinItems_AddsError() {
        var rule = new ArrayBoundsRule(2, null);
        var result = new ValidationResult();
        rule.Validate("tags", new[] { "one" }, result);
        Assert.False(result.IsValid);
        Assert.Contains("at least 2", result.Errors[0].Message);
    }

    [Fact]
    public void AboveMaxItems_AddsError() {
        var rule = new ArrayBoundsRule(null, 3);
        var result = new ValidationResult();
        rule.Validate("tags", new[] { "a", "b", "c", "d" }, result);
        Assert.False(result.IsValid);
        Assert.Contains("at most 3", result.Errors[0].Message);
    }

    [Fact]
    public void WithinBounds_NoError() {
        var rule = new ArrayBoundsRule(1, 5);
        var result = new ValidationResult();
        rule.Validate("tags", new[] { "a", "b", "c" }, result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NullValue_Skipped() {
        var rule = new ArrayBoundsRule(1, 5);
        var result = new ValidationResult();
        rule.Validate("tags", null, result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ListType_Works() {
        var rule = new ArrayBoundsRule(1, null);
        var result = new ValidationResult();
        rule.Validate("tags", new List<string> { "a", "b" }, result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void EmptyList_BelowMin_AddsError() {
        var rule = new ArrayBoundsRule(1, null);
        var result = new ValidationResult();
        rule.Validate("tags", new List<string>(), result);
        Assert.False(result.IsValid);
    }
}
