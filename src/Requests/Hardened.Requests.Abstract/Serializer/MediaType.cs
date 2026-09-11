namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// Matching between the media types a client asked for and the ones a serializer produces.
/// </summary>
/// <remarks>
/// <para>
/// One implementation, deliberately. Every response serializer used to decide this for itself with
/// <c>context.Request.Accept?.Contains("application/json")</c>, and that substring test is false for
/// <c>*/*</c> and for a request with no <c>Accept</c> header at all - which is most of them. Those
/// requests were served only because the JSON serializer is also the default, so the fallback meant
/// for genuine mismatches was quietly carrying the common case.
/// </para>
/// <para>
/// <b>Nothing here allocates.</b> The header is walked as a <see cref="ReadOnlySpan{T}"/> and its
/// segments are compared in place. It used to be split into a <c>List&lt;string&gt;</c> first, which
/// cost 752 bytes per request on any endpoint a real client called - 492 of substrings, 210 of the
/// <c>String[]</c> from the <c>Split</c>, and the list and wrapper on top. That was 44% of the
/// allocation of a JSON request and more than its entire object graph.
/// </para>
/// <para>
/// <b>Parameters are discarded, including q.</b> Preference comes from the order types are listed
/// in, which is how well-formed clients write the header - all three of TechEmpower's, for one,
/// list their preferred type first and use q only to restate it. A header that contradicts its own
/// order, <c>text/html;q=0.5, application/json;q=0.9</c>, resolves to <c>text/html</c> here. That is
/// a decision rather than an oversight: honouring q means sorting, sorting means allocating, and
/// nothing observed in practice sends it. Adding it later changes this file and nothing else.
/// </para>
/// </remarks>
public static class MediaType {
    /// <summary>The wildcard a client sends when it will take anything.</summary>
    public const string Any = "*/*";

    /// <summary>
    /// Whether a client sending <paramref name="accept"/> will take <paramref name="produced"/>.
    /// </summary>
    /// <param name="accept">
    /// The <c>Accept</c> header, unparsed. Null, empty and a header naming no media type all mean
    /// the client will take anything.
    /// </param>
    public static bool Accepts(string? accept, string? produced) {
        if (string.IsNullOrEmpty(produced)) {
            return false;
        }

        foreach (var requested in Enumerate(accept)) {
            if (Matches(requested, produced!)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The index in <paramref name="produced"/> of the first representation a client sending
    /// <paramref name="accept"/> will take, or -1 when it will take none of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The client's preferences are the outer loop, which is what makes the client's ranking
    /// decide.</b> A request for <c>application/json,text/html;q=0.9</c> against an operation
    /// producing both is answered with JSON because JSON is asked about first, not because of any
    /// order the server holds.
    /// </para>
    /// <para>
    /// A client expressing no preference is answered with entry zero: the first representation an
    /// operation declares is the one it leads with, and the one its document lists first.
    /// </para>
    /// </remarks>
    public static int FirstAccepted(string? accept, IReadOnlyList<string> produced) {
        foreach (var requested in Enumerate(accept)) {
            for (var i = 0; i < produced.Count; i++) {
                if (Matches(requested, produced[i])) {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// The media types an <c>Accept</c> header names, most preferred first, trimmed and stripped of
    /// their parameters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the caller that has to do something per entry rather than ask one question -
    /// <c>SerializationLocatorService</c> runs the client's preferences on the outside and an
    /// operation's declared types on the inside, so that the client's ranking decides which
    /// representation is written.
    /// </para>
    /// <para>
    /// A header that names nothing - absent, empty, or punctuation alone - yields <see cref="Any"/>
    /// once, because a client that stated no preference will take anything. That keeps the caller to
    /// one loop rather than a loop and a fallback.
    /// </para>
    /// </remarks>
    public static AcceptEnumerator Enumerate(string? accept) => new(accept);

    /// <summary>
    /// Whether <paramref name="produced"/> satisfies a client asking for <paramref name="requested"/>.
    /// </summary>
    /// <param name="requested">
    /// One entry from an <c>Accept</c> header: a concrete type, a subtype wildcard such as
    /// <c>text/*</c>, or <c>*/*</c>.
    /// </param>
    /// <param name="produced">The concrete media type a serializer writes.</param>
    public static bool Matches(string? requested, string? produced) =>
        !string.IsNullOrEmpty(produced) &&
        (string.IsNullOrEmpty(requested) || Matches(requested.AsSpan(), produced!));

    /// <summary>
    /// One entry of an <c>Accept</c> header, already trimmed and already stripped of its
    /// parameters, against a media type a serializer writes.
    /// </summary>
    /// <param name="requested">An entry from <see cref="Enumerate"/>.</param>
    /// <param name="produced">The concrete media type a serializer writes.</param>
    public static bool Matches(ReadOnlySpan<char> requested, string produced) {
        var candidate = produced.AsSpan();

        // "text/html; charset=utf-8" is text/html. The requested side arrives stripped, and a
        // produced type carrying a charset - which a template's does, because the charset is part
        // of what it writes - would otherwise match nothing but itself.
        var parameters = candidate.IndexOf(';');

        if (parameters >= 0) {
            candidate = candidate.Slice(0, parameters).TrimEnd();
        }

        // An absent Accept header means the client will take anything, which is the same answer as
        // */* rather than a reason to refuse.
        if (requested.IsEmpty || requested.SequenceEqual(Any.AsSpan())) {
            return true;
        }

        if (requested.Equals(candidate, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        var slash = requested.IndexOf('/');

        // "text/*" against "text/html". Anything without a slash is not a media type; treated as no
        // match rather than guessed at.
        if (slash < 0 || slash != requested.Length - 2 || requested[requested.Length - 1] != '*') {
            return false;
        }

        return candidate.Length > slash &&
               candidate[slash] == '/' &&
               requested.Slice(0, slash).Equals(candidate.Slice(0, slash), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Walks the media types of an <c>Accept</c> header without taking a substring of any of them.
    /// </summary>
    /// <remarks>
    /// A <c>ref struct</c>, and its own enumerator, so a <c>foreach</c> over it allocates nothing -
    /// no iterator object, no boxed enumerator, and no string per entry. Not general-purpose: it is
    /// the header format and only that.
    /// </remarks>
    public ref struct AcceptEnumerator {
        private readonly ReadOnlySpan<char> _header;
        private int _position;
        private bool _named;
        private bool _finished;

        internal AcceptEnumerator(string? accept) {
            _header = accept.AsSpan();
            _position = 0;
            _named = false;
            _finished = false;
            Current = default;
        }

        public ReadOnlySpan<char> Current { get; private set; }

        public AcceptEnumerator GetEnumerator() => this;

        public bool MoveNext() {
            if (_finished) {
                return false;
            }

            while (_position < _header.Length) {
                var remaining = _header.Slice(_position);
                var comma = remaining.IndexOf(',');
                ReadOnlySpan<char> entry;

                if (comma < 0) {
                    entry = remaining;
                    _position = _header.Length;
                }
                else {
                    entry = remaining.Slice(0, comma);
                    _position += comma + 1;
                }

                // Everything from the first ';' is parameters - q, charset, version tags such as
                // ";v=b3". None of it participates in matching, and it is skipped rather than
                // removed, so no substring is taken.
                var semicolon = entry.IndexOf(';');

                if (semicolon >= 0) {
                    entry = entry.Slice(0, semicolon);
                }

                entry = entry.Trim();

                if (entry.IsEmpty) {
                    continue;
                }

                _named = true;
                Current = entry;

                return true;
            }

            _finished = true;

            // A header naming nothing at all - absent, empty, or "," - is a client that stated no
            // preference rather than one that refused everything.
            if (_named) {
                return false;
            }

            _named = true;
            Current = Any.AsSpan();

            return true;
        }
    }
}
