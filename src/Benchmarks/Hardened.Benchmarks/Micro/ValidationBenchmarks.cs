using BenchmarkDotNet.Attributes;
using Hardened.Benchmarks.Infrastructure;
using Hardened.Requests.Abstract.Validation;
using Hardened.Requests.Runtime.Validation.Rules;

namespace Hardened.Benchmarks.Micro;

/// <summary>
/// The validation rules, on the passing path.
///
/// Rules are measured rather than <c>ValidationFilter</c> because the filter is only emitted for
/// routes generated from an OpenAPI specification — <c>ValidationFilterEmitter</c> in the OpenAPI
/// generator constructs it — and the benchmark routes are hand-written controllers, which get no
/// validation filter at all. The rules are the per-value inner loop the filter runs, so this is
/// the part that scales with request shape; adding an OpenAPI-generated route to the pipeline
/// benchmarks would be the way to capture the filter's own overhead on top.
///
/// Only valid input is measured. A failing rule allocates an error and a message string, which
/// makes it a different and much rarer path — worth measuring deliberately, not worth mixing into
/// the number that represents normal traffic.
///
/// Reading the allocation column: every benchmark here constructs a <c>ValidationResult</c>,
/// which is 24 bytes, so 24 B means the rule itself allocated nothing. The rules are expected to
/// be allocation-free while they pass; anything above 24 B on a passing benchmark is the rule's
/// own, and worth a look.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Micro)]
public class ValidationBenchmarks {
    private RequiredRule _required = null!;
    private RangeRule _range = null!;
    private StringLengthRule _stringLength = null!;
    private PatternRule _pattern = null!;
    private EnumRule _enumRule = null!;
    private ArrayBoundsRule _arrayBounds = null!;
    private IValidationRule[] _all = null!;
    private List<int> _array = null!;

    [GlobalSetup]
    public void Setup() {
        _required = RequiredRule.Instance;
        _range = new RangeRule(1, 100);
        _stringLength = new StringLengthRule(1, 64);
        _pattern = new PatternRule("^[a-z0-9-]+$");
        _enumRule = new EnumRule(["active", "inactive", "pending"]);
        _arrayBounds = new ArrayBoundsRule(1, 32);
        _all = [_required, _stringLength, _pattern];
        _array = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    }

    [Benchmark(Baseline = true)]
    public bool Required() {
        var result = new ValidationResult();
        _required.Validate("name", "benchmark", result);

        return result.IsValid;
    }

    [Benchmark]
    public bool Range() {
        var result = new ValidationResult();
        _range.Validate("count", 42, result);

        return result.IsValid;
    }

    [Benchmark]
    public bool StringLength() {
        var result = new ValidationResult();
        _stringLength.Validate("name", "benchmark", result);

        return result.IsValid;
    }

    /// <summary>Regex-backed, so expected to be the most expensive of the set by some margin.</summary>
    [Benchmark]
    public bool Pattern() {
        var result = new ValidationResult();
        _pattern.Validate("slug", "benchmark-slug", result);

        return result.IsValid;
    }

    [Benchmark]
    public bool Enumeration() {
        var result = new ValidationResult();
        _enumRule.Validate("status", "active", result);

        return result.IsValid;
    }

    [Benchmark]
    public bool ArrayBounds() {
        var result = new ValidationResult();
        _arrayBounds.Validate("values", _array, result);

        return result.IsValid;
    }

    /// <summary>Three rules over one value, the shape a single annotated property produces.</summary>
    [Benchmark]
    public bool RuleSetOverOneValue() {
        var result = new ValidationResult();

        foreach (var rule in _all) {
            rule.Validate("name", "benchmark", result);
        }

        return result.IsValid;
    }

    /// <summary>The failing path, for contrast: an error object and a formatted message.</summary>
    [Benchmark]
    public bool RequiredFailing() {
        var result = new ValidationResult();
        _required.Validate("name", null, result);

        return result.IsValid;
    }
}
