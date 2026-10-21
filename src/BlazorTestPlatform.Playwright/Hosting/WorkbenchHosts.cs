using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlazorTestPlatform.Playwright.Hosting;

/// <summary>A running application that hosts the workbench (story frames).</summary>
public interface IWorkbenchHost : IAsyncDisposable
{
    /// <summary>Base address, e.g. <c>http://127.0.0.1:5123/</c>.</summary>
    Uri BaseAddress { get; }

    /// <summary>Starts the host (idempotent).</summary>
    Task StartAsync(CancellationToken cancellationToken = default);
}

/// <summary>A workbench that is already running (local dev server, preview deployment, container).</summary>
public sealed class ExternalWorkbenchHost : IWorkbenchHost
{
    /// <summary>Creates the host. Reads <c>BTP_BASE_URL</c> when <paramref name="baseAddress"/> is null.</summary>
    public ExternalWorkbenchHost(Uri? baseAddress = null)
    {
        var configured = baseAddress?.ToString() ?? Environment.GetEnvironmentVariable("BTP_BASE_URL")
            ?? throw new InvalidOperationException("Specify the workbench address or set BTP_BASE_URL.");
        BaseAddress = new Uri(configured.EndsWith('/') ? configured : configured + "/");
    }

    /// <inheritdoc />
    public Uri BaseAddress { get; }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default) => WorkbenchHostProbe.WaitUntilReadyAsync(BaseAddress, TimeSpan.FromSeconds(30), cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Hosts the workbench in-process on Kestrel through <see cref="WebApplicationFactory{TEntryPoint}"/>, so browsers can
/// reach it over a real socket. Override <c>ConfigureWebHost</c> to swap services for every story.
/// </summary>
/// <typeparam name="TEntryPoint">A type in the workbench host app (usually <c>Program</c>).</typeparam>
public class WorkbenchApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>, IWorkbenchHost
    where TEntryPoint : class
{
    private Uri? _baseAddress;

    /// <summary>Creates the factory listening on a free loopback port.</summary>
    public WorkbenchApplicationFactory()
    {
        UseKestrel(0);
    }

    /// <inheritdoc />
    public Uri BaseAddress => _baseAddress ?? throw new InvalidOperationException("Call StartAsync first.");

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_baseAddress is not null)
        {
            return;
        }

        StartServer();
        var addresses = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
            ?? throw new InvalidOperationException("Kestrel did not report its addresses.");
        var address = addresses.First().Replace("[::]", "127.0.0.1", StringComparison.Ordinal).Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal);
        _baseAddress = new Uri(address.EndsWith('/') ? address : address + "/");
        await WorkbenchHostProbe.WaitUntilReadyAsync(_baseAddress, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        base.ConfigureWebHost(builder);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}

/// <summary>Options for <see cref="ProcessWorkbenchHost"/>.</summary>
public sealed class ProcessWorkbenchHostOptions
{
    /// <summary>Build configuration. Default <c>Debug</c>.</summary>
    public string Configuration { get; set; } = "Debug";

    /// <summary>Skip building before running.</summary>
    public bool NoBuild { get; set; }

    /// <summary>Startup timeout. Default 3 minutes (first WebAssembly builds are slow).</summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>Extra environment variables.</summary>
    public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal) { ["ASPNETCORE_ENVIRONMENT"] = "Development" };
}

/// <summary>
/// Runs the workbench host project with <c>dotnet run</c> on a free port. Use when the host cannot be loaded in-process
/// (e.g. a different target framework), or to test exactly what developers run.
/// </summary>
public sealed class ProcessWorkbenchHost : IWorkbenchHost
{
    private readonly string _projectPath;
    private readonly ProcessWorkbenchHostOptions _options;
    private readonly List<string> _output = [];
    private Process? _process;

    /// <summary>Creates the host for a project file or directory.</summary>
    public ProcessWorkbenchHost(string projectPath, ProcessWorkbenchHostOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        _projectPath = Path.GetFullPath(projectPath);
        _options = options ?? new ProcessWorkbenchHostOptions();
        BaseAddress = new Uri($"http://127.0.0.1:{FreePort()}/");
    }

    /// <inheritdoc />
    public Uri BaseAddress { get; }

    /// <summary>Recent process output (for diagnosing startup failures).</summary>
    public IReadOnlyList<string> Output
    {
        get
        {
            lock (_output)
            {
                return _output.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_process is not null)
        {
            return;
        }

        var arguments = new List<string> { "run", "--project", _projectPath, "--configuration", _options.Configuration, "--no-launch-profile", "--urls", BaseAddress.ToString().TrimEnd('/') };
        if (_options.NoBuild)
        {
            arguments.Add("--no-build");
        }

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        arguments.ForEach(start.ArgumentList.Add);
        foreach (var (key, value) in _options.Environment)
        {
            start.Environment[key] = value;
        }

        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start 'dotnet run'.");
        _process.OutputDataReceived += (_, e) => Capture(e.Data);
        _process.ErrorDataReceived += (_, e) => Capture(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        try
        {
            await WorkbenchHostProbe.WaitUntilReadyAsync(BaseAddress, _options.StartupTimeout, cancellationToken, () => _process.HasExited).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
        {
            throw new InvalidOperationException($"Workbench host '{_projectPath}' did not start:{System.Environment.NewLine}{string.Join(System.Environment.NewLine, Output.TakeLast(40))}", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }

        _process?.Dispose();
    }

    private void Capture(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_output)
        {
            _output.Add(line);
            if (_output.Count > 500)
            {
                _output.RemoveAt(0);
            }
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>Polls the workbench until it answers.</summary>
public static class WorkbenchHostProbe
{
    /// <summary>Waits until <c>/_workbench</c> returns success.</summary>
    public static async Task WaitUntilReadyAsync(Uri baseAddress, TimeSpan timeout, CancellationToken cancellationToken, Func<bool>? hasExited = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        using var http = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(10) };
        var clock = Stopwatch.StartNew();
        Exception? last = null;
        while (clock.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (hasExited?.Invoke() == true)
            {
                throw new InvalidOperationException("The host process exited during startup.");
            }

            try
            {
                using var response = await http.GetAsync("_workbench", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                last = new HttpRequestException($"GET /_workbench returned {(int)response.StatusCode}.");
            }
            catch (HttpRequestException ex)
            {
                last = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                last = ex;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Workbench at {baseAddress} did not respond within {timeout.TotalSeconds:0} s: {last?.Message}");
    }
}
