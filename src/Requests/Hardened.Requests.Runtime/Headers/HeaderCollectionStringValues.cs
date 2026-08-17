using System.Collections;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Utilities;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Headers;

/// <summary>
/// An <see cref="IHeaderCollection"/> over a dictionary, looked up the way HTTP defines header
/// names: without regard to case.
/// </summary>
/// <remarks>
/// <para>
/// Every lookup here used to run against a dictionary built with the default, case-sensitive
/// comparer, while <c>KnownHeaders</c> asks for canonical spellings - <c>Content-Type</c>,
/// <c>Accept</c>, <c>Cookie</c>. API Gateway's HTTP API delivers header names lowercased, so on
/// Lambda those two never met: every request was read as <c>application/json</c> whatever its
/// content type, and <c>Accept</c> was ignored, which meant content negotiation did not work on
/// that transport at all.
/// </para>
/// <para>
/// It was invisible because every header test wrote canonical casing, so the harnesses were kinder
/// than the transports they stood in for. ASP.NET and Kestrel were unaffected on the ordinary path -
/// they hand over their own <c>IHeaderDictionary</c>, which is already case-insensitive - but a
/// forked request there took an override dictionary and lost the property.
/// </para>
/// </remarks>
public class HeaderCollectionStringValues : IHeaderCollection {
    private readonly IDictionary<string, StringValues> _headers;

    public HeaderCollectionStringValues() {
        _headers = NewCaseInsensitive();
    }

    public HeaderCollectionStringValues(IDictionary<string, string>? values) {
        _headers = NewCaseInsensitive();

        if (values != null) {
            foreach (var pair in values) {
                _headers[pair.Key] = pair.Value.ToStringValues();
            }
        }
    }

    /// <summary>
    /// Wraps <paramref name="headers"/> when it already compares names without regard to case, and
    /// copies it when it does not.
    /// </summary>
    /// <remarks>
    /// Wrapping by reference is deliberate where it is safe: a caller that hands over a dictionary
    /// and then reads its own copy back - which is how the Lambda transports collect response
    /// headers - depends on writes landing in it. Copying unconditionally would have taken that
    /// away, so the reference survives for a dictionary that was built correctly and the copy is the
    /// fallback for one that was not.
    /// </remarks>
    public HeaderCollectionStringValues(IDictionary<string, StringValues> headers) {
        _headers = EnsureCaseInsensitive(headers);
    }

    /// <summary>
    /// <paramref name="headers"/> if it already compares names without regard to case, otherwise a
    /// case-insensitive copy of it.
    /// </summary>
    /// <remarks>
    /// Public because a transport holding a raw dictionary needs the same guarantee without wrapping
    /// it in a collection - the header override a forked request carries on ASP.NET and Kestrel is
    /// exactly that case.
    /// </remarks>
    public static IDictionary<string, StringValues> EnsureCaseInsensitive(
        IDictionary<string, StringValues> headers) {
        if (IsCaseInsensitive(headers)) {
            return headers;
        }

        var copy = NewCaseInsensitive();

        foreach (var pair in headers) {
            copy[pair.Key] = pair.Value;
        }

        return copy;
    }

    private static Dictionary<string, StringValues> NewCaseInsensitive() {
        return new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Only a <see cref="Dictionary{TKey,TValue}"/> exposes the comparer it was built with, so
    /// anything else - including ASP.NET's own header dictionary, which is case-insensitive but not
    /// a <c>Dictionary</c> - is copied rather than trusted. Those transports do not construct this
    /// type on the path that matters, so the copy lands where it is affordable.
    /// </summary>
    private static bool IsCaseInsensitive(IDictionary<string, StringValues> headers) {
        return headers is Dictionary<string, StringValues> dictionary &&
               ReferenceEquals(dictionary.Comparer, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() {
        return _headers.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }

    public StringValues Append(string key, object value) {
        value ??= "";

        if (TryGet(key, out var stringValues)) {
            stringValues = StringValues.Concat(stringValues, value.ToString());
        }
        else {
            stringValues = value.ToString();
        }

        _headers[key] = stringValues;

        return stringValues;
    }

    public void Add(string key, StringValues value) {
        throw new NotImplementedException();
    }

    public bool ContainsKey(string key) {
        return _headers.ContainsKey(key);
    }

    public bool Remove(string key) {
        return _headers.Remove(key);
    }

    public bool TryGetValue(string key, out StringValues value) {
        return _headers.TryGetValue(key, out value);
    }

    public StringValues this[string key] {
        get {
            if (_headers.TryGetValue(key, out var value)) {
                return value;
            }
            return StringValues.Empty;
        }
        set => _headers[key] = value;
    }

    public ICollection<string> Keys => _headers.Keys;
    public ICollection<StringValues> Values => _headers.Values;

    public StringValues Get(string key) {
        if (_headers.TryGetValue(key, out var stringValues)) {
            return stringValues;
        }

        return StringValues.Empty;
    }

    public StringValues Set(string key, object? value) {
        if (value == null) {
            _headers.Remove(key);

            return StringValues.Empty;
        }

        return Set(key, value.ToString());
    }

    public StringValues Set(string key, StringValues value) {
        return _headers[key] = value;
    }

    public void Add(KeyValuePair<string, StringValues> item) {
        _headers.Add(item);
    }

    public void Clear() {
        _headers.Clear();
    }

    public bool Contains(KeyValuePair<string, StringValues> item) {
        return _headers.Contains(item);
    }

    public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex) {
        _headers.CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<string, StringValues> item) {
        return _headers.Remove(item);
    }

    public int Count => _headers.Count;

    public bool IsReadOnly => false;

    public bool TryGet(string key, out StringValues value) {
        return _headers.TryGetValue(key, out value);
    }

    public IDictionary<string, string> ToStringDictionary() {
        var dictionary = new Dictionary<string, string>();

        foreach (var pair in _headers) {
            dictionary[pair.Key] = pair.Value.ToString();
        }

        return dictionary;
    }
}