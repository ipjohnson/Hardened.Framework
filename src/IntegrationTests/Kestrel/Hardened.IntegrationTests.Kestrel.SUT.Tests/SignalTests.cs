using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Xunit;

namespace Hardened.IntegrationTests.Kestrel.SUT.Tests;

/// <summary>
/// What a request in flight gets when the process is stopped with a signal.
/// </summary>
/// <remarks>
/// <para>
/// The application's <c>Program.cs</c> ends with the plain <c>app.RunAsync()</c>, as the template's
/// Kestrel host does. <c>docker stop</c>, Kubernetes, Cloud Run and Container Apps stop a container
/// with <c>SIGTERM</c>; a terminal sends <c>SIGINT</c>.
/// </para>
/// <para>
/// The application runs as a process of its own, from its build output, because a signal goes to
/// a whole process and one raised inside the test host would stop the test run.
/// </para>
/// </remarks>
public sealed class SignalTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SigtermLetsAnInFlightRequestFinish()
    {
        await AnInFlightRequestFinishesAfter("TERM");
    }

    [Fact]
    public async Task SigintLetsAnInFlightRequestFinish()
    {
        await AnInFlightRequestFinishesAfter("INT");
    }

    /// <summary>
    /// A request whose handler takes three seconds, the signal once the handler has started, and
    /// then: the process exits on its own with 0, and the response is a 200.
    /// </summary>
    /// <remarks>
    /// The exit code is asserted first because it names the failure: a process the signal
    /// terminated exits with 128 plus the signal's number, 143 for <c>SIGTERM</c>, and the request
    /// then fails on whatever the client tried next.
    /// </remarks>
    private static async Task AnInFlightRequestFinishesAfter(string signal)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no POSIX signals to send.");

        await using var application = await Application.Start();

        using var client = new HttpClient
        {
            BaseAddress = application.Address,
            Timeout = TimeSpan.FromSeconds(30),
        };

        var inFlight = client.GetAsync("/slow", Token);

        await application.Printed("IN FLIGHT");

        await application.Signal(signal);

        Assert.Equal(0, await application.ExitCode());

        using var response = await inFlight;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The application started with <c>dotnet</c> from its build output, on a port of its own.
    /// </summary>
    private sealed class Application : IAsyncDisposable
    {
        private const string Name = "Hardened.IntegrationTests.Kestrel.SUT";

        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

        private readonly Process _process;
        private readonly object _gate = new();
        private readonly List<string> _lines = [];
        private readonly List<(string Line, TaskCompletionSource Seen)> _waiting = [];

        private Application(Process process, int port)
        {
            _process = process;
            Address = new Uri($"http://127.0.0.1:{port}/");
        }

        public Uri Address { get; }

        public static async Task<Application> Start()
        {
            var output = BuildOutput();
            var port = FreePort();

            var start = new ProcessStartInfo(Dotnet())
            {
                WorkingDirectory = output,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            start.ArgumentList.Add(Path.Combine(output, Name + ".dll"));
            start.ArgumentList.Add(port.ToString());

            var process = new Process { StartInfo = start };

            var application = new Application(process, port);

            process.OutputDataReceived += (_, line) => application.Received(line.Data);
            process.ErrorDataReceived += (_, line) => application.Received(line.Data);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Program.cs prints this once Kestrel has bound.
            await application.Printed("LISTENING");

            return application;
        }

        /// <summary>Returns once the application has printed a line starting with <paramref name="line"/>.</summary>
        public async Task Printed(string line)
        {
            var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_gate)
            {
                if (_lines.Any(printed => printed.StartsWith(line, StringComparison.Ordinal)))
                {
                    return;
                }

                _waiting.Add((line, seen));
            }

            var exited = _process.WaitForExitAsync(Token);
            var first = await Task.WhenAny(seen.Task, exited, Task.Delay(Patience, Token));

            Assert.True(
                first == seen.Task,
                $"The application did not print '{line}'. Its output:\n{Transcript}"
            );
        }

        /// <summary>Sends <paramref name="signal"/> the way <c>docker stop</c> does, with kill.</summary>
        public async Task Signal(string signal)
        {
            using var kill = Process.Start("kill", ["-" + signal, _process.Id.ToString()])!;

            await kill.WaitForExitAsync(Token);

            Assert.Equal(0, kill.ExitCode);
        }

        public async Task<int> ExitCode()
        {
            await _process.WaitForExitAsync(Token).WaitAsync(Patience, Token);

            return _process.ExitCode;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);

                await _process.WaitForExitAsync(Token);
            }

            _process.Dispose();
        }

        private void Received(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_gate)
            {
                _lines.Add(line);

                foreach (var (waited, seen) in _waiting)
                {
                    if (line.StartsWith(waited, StringComparison.Ordinal))
                    {
                        seen.TrySetResult();
                    }
                }
            }
        }

        private string Transcript
        {
            get
            {
                lock (_gate)
                {
                    return string.Join('\n', _lines);
                }
            }
        }

        /// <summary>
        /// The application's build output, beside this project's in the same configuration.
        /// </summary>
        private static string BuildOutput()
        {
            var testOutput = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

            var framework = Path.GetFileName(testOutput);
            var configuration = Path.GetFileName(Path.GetDirectoryName(testOutput)!);
            var fixtures = Path.GetFullPath(Path.Combine(testOutput, "..", "..", "..", ".."));

            return Path.Combine(fixtures, Name, "bin", configuration, framework);
        }

        /// <summary>
        /// The <c>dotnet</c> of the installation this test runs on, which has the runtime the
        /// application needs.
        /// </summary>
        private static string Dotnet() =>
            Path.GetFullPath(
                Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet")
            );

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);

            probe.Start();

            var port = ((IPEndPoint)probe.LocalEndpoint).Port;

            probe.Stop();

            return port;
        }
    }
}
