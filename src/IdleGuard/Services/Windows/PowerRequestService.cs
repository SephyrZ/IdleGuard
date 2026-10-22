using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;
using System.Runtime.InteropServices;

namespace IdleGuard.Services.Windows;

/// <summary>On-demand snapshot only; never polls or changes Windows power requests.</summary>
public sealed class PowerRequestService
{
    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    public async Task<bool> HasDisplayRequestAsync(CancellationToken cancellationToken)
        => (await GetDisplayRequestsAsync(cancellationToken)).Count > 0;

    public async Task<IReadOnlyList<string>> GetDisplayRequestsAsync(CancellationToken cancellationToken)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding((int)GetOEMCP());
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
                Arguments = "/requests",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding
            }
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        if (!process.Start()) throw new IOException("Could not start powercfg.");
        // Drain both pipes concurrently so neither can block process exit.
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var text = await output;
            var detail = await error;
            if (process.ExitCode != 0)
                throw new IOException($"powercfg exited with code {process.ExitCode}. {detail.Trim()} {text.Trim()}".Trim());
            return ParseDisplayRequestSources(text);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("powercfg did not respond within 10 seconds.");
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            // Observe canceled reads as well as successful reads before disposing their streams.
            try { await Task.WhenAll(output, error); }
            catch (OperationCanceledException) { }
        }
    }

    internal static bool ParseDisplayRequest(string output)
        => ParseDisplayRequestSources(output).Count > 0;

    internal static IReadOnlyList<string> ParseDisplayRequestSources(string output)
    {
        // Section names and bracket delimiters are ASCII, even on Chinese Windows.
        // Preserve source paths/names but exclude localized caller labels and reasons.
        var section = Regex.Match(output,
            @"^\s*DISPLAY\s*[:：]\s*\r?\n(?<body>[\s\S]*?)(?=^\s*SYSTEM\s*[:：]\s*$)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!section.Success) throw new FormatException("powercfg returned no recognizable DISPLAY/SYSTEM sections. Run IdleGuard as administrator and retry.");
        var body = section.Groups["body"].Value;
        var entries = Regex.Matches(body, @"^[\t ]*\[[^\]\r\n]+\][\t ]*(?<source>[^\r\n]+)", RegexOptions.Multiline);
        if (entries.Count > 0)
            return entries.Select(entry => entry.Groups["source"].Value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Do not depend on the localized spelling of 'None'. Successful powercfg
        // output with a complete section and no caller entries has no display request.
        if (body.Contains('[') || string.IsNullOrWhiteSpace(body))
            throw new FormatException("powercfg returned an incomplete DISPLAY section.");
        return Array.Empty<string>();
    }
}
