using System.Diagnostics;
using System.Security.Cryptography;

namespace Hardened.Requests.Abstract.Diagnostics;

/// <summary>
/// Where a request's correlation id comes from.
/// </summary>
/// <remarks>
/// <para>
/// <b>The trace id when there is one, a fresh one when there is not.</b> The pipeline already starts
/// a span per request and already joins the caller's trace when they sent a <c>traceparent</c>, so
/// when anything is collecting traces the correlation id and the trace id should be the same string
/// - two identities for one request is how a log line and a span end up impossible to line up.
/// </para>
/// <para>
/// But <c>ActivitySource.StartActivity</c> returns null when nothing is listening, which is
/// deliberate and is what makes instrumenting the pipeline unconditional. So on any deployment
/// without a collector - every developer machine, most test runs, plenty of production - there is no
/// trace id in existence, and that is exactly when someone reading logs most wants an id to group
/// them by. Hence the fallback.
/// </para>
/// <para>
/// <b>The fallback is 13 characters, not 32.</b> It used to be an <see cref="ActivityTraceId"/>, so
/// that the value was the same 32 hex characters either way. That shape bought less than it looked:
/// a log query matches the id it was handed and never its length, and the join that actually
/// matters, a log line to its span, still holds because the traced path still returns the trace id.
/// What it cost was 88 bytes and 23ns on the path every unsampled request takes. The short id is
/// 48 bytes and 17ns, and unlike the trace id it says when the request started.
/// </para>
/// <para>
/// A deployment under a ratio sampler does therefore emit both shapes, 32 characters for the
/// requests that were sampled and 13 for the rest. That is accepted. The long values are exactly
/// the requests that can also be found in the trace store.
/// </para>
/// </remarks>
public static class CorrelationIdentifier {

    /// <summary>
    /// The current trace's id, or a new one when nothing is tracing.
    /// </summary>
    /// <remarks>
    /// Read lazily by the contexts rather than at construction, because the host builds the context
    /// before <c>IRequestLogger.RequestBegin</c> starts the span - so anything eager would mint an
    /// id and then be contradicted a moment later by a span carrying a different one.
    /// </remarks>
    public static string ForCurrentTrace() {
        var current = Activity.Current;

        return current is null
            ? Fallback.NextId()
            : current.TraceId.ToHexString();
    }

    /// <summary>
    /// The id issued when there is no trace to take one from: a millisecond and a counter, written
    /// out as 13 base64 characters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Seven characters of millisecond, six of counter.</b> Six bits a character puts 42 bits of
    /// Unix millisecond in the first seven, which runs out in 2109, and 36 bits of counter in the
    /// last six, which is 68.7 billion. Two ids can only collide if they share both fields. Within one process that cannot happen: the counter only repeats after 68.7 billion
    /// ids, so every id issued inside one millisecond has a distinct one. Between processes it
    /// takes two counters landing on the same value in the same millisecond, which at a fleet-wide
    /// million requests a second comes to one duplicate pair per 700 million requests or so. That
    /// is the best-effort part, and it is why the millisecond is worth its seven characters: a
    /// coarser clock would multiply that rate by the number of ids sharing a bucket.
    /// </para>
    /// <para>
    /// <b>A counter rather than more random bits.</b> Random ids collide on the birthday bound,
    /// which is quadratic in how many were issued. A counter is linear in how many processes were
    /// running, and gives the intra-process guarantee above for free. Seeded at random rather than
    /// from the clock, which is what ASP.NET Core does for its connection ids and is the weaker
    /// choice: two processes that start in the same tick get the same seed and then issue identical
    /// ids for the rest of their lives.
    /// </para>
    /// <para>
    /// <b>Handed out in blocks, because one contended cache line costs more than everything else
    /// here put together.</b> Taking each id with <c>Interlocked.Increment</c> measured 19M ids/s
    /// across 11 threads, against 84M for the random id it replaces: the shared counter made it
    /// slower than the thing it was meant to improve on. Taking 64 at a time and handing them out
    /// from thread-local state measures 98M. A thread that dies mid-block abandons what is left of
    /// it, which costs nothing but 63 counter values.
    /// </para>
    /// <para>
    /// <b>One sequence for the process, rather than a seed per thread.</b> Seeding each thread
    /// independently would drop the shared counter entirely, and measured 0.61ns against 0.64ns
    /// for taking a block, so it saves nothing. What it costs is the guarantee: independent seeds
    /// put threads back on a birthday bound against each other, and the millisecond protects far
    /// less there than it does between processes, because the threads of one process are all
    /// writing into the same milliseconds all the time. One sequence makes an intra-process
    /// duplicate impossible instead, and thread-pool threads that come and go just take a block
    /// rather than each drawing a seed.
    /// </para>
    /// <para>
    /// <b>Base64 rather than base62, for the shifts.</b> Sixty-two needs a division per character
    /// and measured 25ns, slower than the 32-character id it was meant to replace. Sixty-four is a
    /// shift and a mask and measures 17ns, and buys a wider field per character besides. The
    /// alphabet is in ASCII order, so an id sorts as text the way it sorts as a number, which is
    /// what makes a log file sort into request order. It is the price of the density that two of
    /// its characters are <c>-</c> and <c>_</c>: an id is case-sensitive, and a log tool that
    /// breaks tokens on a hyphen will split about a third of them.
    /// </para>
    /// <para>
    /// <b>It is the request's millisecond, not the log line's.</b> The id is minted once and put in
    /// scope for the whole request, so every line the request writes repeats it. A request that
    /// runs for three seconds writes lines that all carry the millisecond it started. It does not
    /// replace the timestamp on the line.
    /// </para>
    /// <para>
    /// <b>The clock is monotonic, so the millisecond is approximate.</b> <see cref="Stopwatch"/> is
    /// read per id at 10ns, against 20ns for <c>DateTimeOffset.UtcNow</c>, and turned into a date
    /// with an offset taken once at startup. That tracks a frequency correction but not a step
    /// adjustment, so a process running for months can drift from the wall clock. The field says
    /// roughly when, and the log line still says exactly when.
    /// </para>
    /// </remarks>
    private static class Fallback {
        /// <summary>
        /// Ids a thread reserves at a time. A power of two, which is what lets the cursor's own low
        /// bits say when a block is spent.
        /// </summary>
        private const int BlockSize = 64;

        /// <summary>
        /// In ASCII order, which the base64 alphabets in the wild are not - theirs start at
        /// <c>A</c>, so their text order and their numeric order disagree and an id stops sorting.
        /// </summary>
        private const string Digits =
            "-0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz";

        private static readonly long TicksPerMillisecond = Stopwatch.Frequency / 1000;

        /// <summary>
        /// Where <see cref="Stopwatch"/>'s own zero falls, in Unix milliseconds. Its timestamps
        /// count from an arbitrary origin, usually boot, so this is what turns one into a date.
        /// </summary>
        private static readonly long OriginMillisecond =
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            - Stopwatch.GetTimestamp() / TicksPerMillisecond;

        /// <summary>
        /// The next block to hand out. Seeded at random and aligned to <see cref="BlockSize"/>,
        /// which <c>Interlocked.Add</c> then preserves for the life of the process.
        /// </summary>
        private static long _next =
            BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8)) & ~(BlockSize - 1L);

        /// <summary>
        /// This thread's cursor into the block it holds. Zero on a thread that has never asked for
        /// one, which is aligned, so the first call takes a block like any other.
        /// </summary>
        [ThreadStatic] private static ulong _threadNext;

        public static string NextId() {
            var millisecond = (ulong)(OriginMillisecond + Stopwatch.GetTimestamp() / TicksPerMillisecond);

            // Blocks are aligned, so a cursor sitting on a boundary is a spent block, and every
            // other value is one this thread still owns. That is the whole refill test.
            if ((_threadNext & (BlockSize - 1)) == 0) {
                _threadNext = unchecked((ulong)(Interlocked.Add(ref _next, BlockSize) - BlockSize));
            }

            return Encode(millisecond, _threadNext++);
        }

        private static string Encode(ulong millisecond, ulong counter) =>
            string.Create(13, (millisecond, counter), static (buffer, id) => {
                var digits = Digits;
                var count = id.counter;

                buffer[12] = digits[(int)(count & 63)];
                buffer[11] = digits[(int)((count >> 6) & 63)];
                buffer[10] = digits[(int)((count >> 12) & 63)];
                buffer[9] = digits[(int)((count >> 18) & 63)];
                buffer[8] = digits[(int)((count >> 24) & 63)];
                buffer[7] = digits[(int)((count >> 30) & 63)];

                var moment = id.millisecond;

                buffer[6] = digits[(int)(moment & 63)];
                buffer[5] = digits[(int)((moment >> 6) & 63)];
                buffer[4] = digits[(int)((moment >> 12) & 63)];
                buffer[3] = digits[(int)((moment >> 18) & 63)];
                buffer[2] = digits[(int)((moment >> 24) & 63)];
                buffer[1] = digits[(int)((moment >> 30) & 63)];
                buffer[0] = digits[(int)((moment >> 36) & 63)];
            });
    }
}
