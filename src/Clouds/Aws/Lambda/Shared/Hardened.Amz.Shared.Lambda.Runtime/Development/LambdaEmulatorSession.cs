using System.Diagnostics;

namespace Hardened.Amz.Shared.Lambda.Runtime.Development;

/// <summary>
/// The emulator a function is running against, for as long as the function runs.
/// </summary>
/// <remarks>
/// Passive when the function is not local, so the generated <c>Main</c> holds one either way and
/// reads <see cref="RuntimeApiEndpoint"/> without a branch. Owns the tool's process only when it
/// started one: a tool that was already listening is left as it was found.
/// </remarks>
public sealed class LambdaEmulatorSession : IDisposable {
    private Process? _process;

    /// <summary>The session of a function that is not local. Nothing to point the bootstrap at, nothing to stop.</summary>
    public static LambdaEmulatorSession Passive { get; } = new(null, null);

    internal LambdaEmulatorSession(LambdaEmulatorPlan? plan, Process? process) {
        Plan = plan;
        _process = process;

        if (process != null) {
            // The tool is a child process, and nothing stops it when this one goes: the debugger's
            // stop button leaves it running, and a second start then finds it and reuses it. What
            // can be caught is caught, so a clean exit takes it down.
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += OnCancelKeyPress;
        }
    }

    /// <summary>What was started, or null for <see cref="Passive"/>.</summary>
    public LambdaEmulatorPlan? Plan { get; }

    /// <summary>
    /// Where the bootstrap should poll, or null to leave the bootstrap reading
    /// <c>AWS_LAMBDA_RUNTIME_API</c> as it does on Lambda.
    /// </summary>
    public string? RuntimeApiEndpoint => Plan?.RuntimeApiEndpoint;

    public bool IsLocal => Plan != null;

    /// <summary>Whether this session started the tool, as opposed to finding one listening.</summary>
    public bool StartedTheTool => _process != null;

    public void Dispose() {
        var process = Interlocked.Exchange(ref _process, null);

        if (process == null) {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        Console.CancelKeyPress -= OnCancelKeyPress;

        try {
            if (!process.HasExited) {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) {
            // Exited between the check and the kill.
        }

        process.Dispose();
    }

    private void OnProcessExit(object? sender, EventArgs e) => Dispose();

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) => Dispose();
}
