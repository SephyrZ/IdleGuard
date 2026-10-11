using System.Diagnostics;
using System.Security.Principal;

namespace IdleGuard.Services.Windows;

public static class AdministratorService
{
    public static string? LaunchError { get; set; }
    public static bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static void StartElevated(bool background)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
        // The child waits for this process to release the singleton before starting its listener.
        using var process = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = true, Verb = "runas",
            Arguments = $"--wait-for-exit {Environment.ProcessId}" + (background ? " --background" : "")
        });
        if (process is null) throw new InvalidOperationException("Administrator restart did not start.");
    }
}
