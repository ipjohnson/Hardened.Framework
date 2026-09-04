using Hardened.Requests.Runtime.Filters;

// The assembly rung of the timeout cascade, on a real assembly with real handlers - which is the
// only place it can honestly be tested, since it is resolved by reflecting over the handler type's
// own assembly.
//
// Five minutes: high enough that nothing in this suite can reach it, so every handler here is
// bounded without any test's timing depending on it. What the rung does is asserted by reading the
// resolved budget back through IExecutionContext.HandlerInfo, not by waiting for it.
[assembly: Timeout(Milliseconds = 300_000)]
