using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using System.Runtime.Versioning;
using Microsoft.Win32;
using IdleGuard.Native;

namespace IdleGuard.Services.Windows;

public sealed record ScreenSaverConfiguration(string? Path, int TimeoutSeconds, bool AutomaticEnabled, bool IsManaged = false);
public sealed record ScreenSaverChoice(string? Path, string Name);

[SupportedOSPlatform("windows")]
public sealed class ScreenSaverService
{
    public ScreenSaverConfiguration ReadConfiguration()
    {
        using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        using var userPolicy = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\Control Panel\Desktop");
        using var machinePolicy = Registry.LocalMachine.OpenSubKey(@"Software\Policies\Microsoft\Windows\Control Panel\Desktop");
        var path = (userPolicy?.GetValue("SCRNSAVE.EXE") ?? machinePolicy?.GetValue("SCRNSAVE.EXE") ?? desktop?.GetValue("SCRNSAVE.EXE")) as string;
        path = string.IsNullOrWhiteSpace(path) ? null : Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        var managed = new[] { "SCRNSAVE.EXE", "ScreenSaveTimeOut", "ScreenSaveActive" }
            .Any(name => userPolicy?.GetValue(name) is not null || machinePolicy?.GetValue(name) is not null);
        return new(path, ReadParameter(NativeMethods.GetScreenSaverTimeout),
            ReadParameter(NativeMethods.GetScreenSaverActive) != 0, managed);
    }

    public IReadOnlyList<ScreenSaverChoice> GetChoices(string noneLabel, string? currentPath)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.SystemDirectory })
        {
            try { foreach (var path in Directory.EnumerateFiles(directory, "*.scr")) paths.Add(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        if (!string.IsNullOrWhiteSpace(currentPath)) paths.Add(ResolvePath(currentPath));
        return new[] { new ScreenSaverChoice(null, noneLabel) }.Concat(paths.Select(path => new ScreenSaverChoice(path, ReadName(path)))
            .OrderBy(choice => choice.Name, StringComparer.CurrentCultureIgnoreCase)).ToArray();
    }

    internal static string ResolvePath(string path)
    {
        if (System.IO.Path.IsPathRooted(path)) return System.IO.Path.GetFullPath(path);
        foreach (var directory in new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) })
        {
            var candidate = System.IO.Path.Combine(directory, path);
            if (File.Exists(candidate)) return candidate;
        }
        return System.IO.Path.GetFullPath(path);
    }

    private static string ReadName(string path)
    {
        // Load resources only: do not execute screensaver code while listing installed choices.
        var module = NativeMethods.LoadResourceLibrary(path, 0, 0x00000002 | 0x00000020);
        if (module != 0)
        {
            try
            {
                var text = new StringBuilder(512);
                if (NativeMethods.LoadString(module, 1, text, text.Capacity) > 0) return text.ToString();
            }
            finally { NativeMethods.FreeLibrary(module); }
        }
        return System.IO.Path.GetFileNameWithoutExtension(path);
    }

    private void EnsureWritable()
    {
        if (ReadConfiguration().IsManaged) throw new InvalidOperationException("SystemManaged");
    }

    public void SetTimeout(int seconds)
    {
        if (seconds < 1) throw new ArgumentOutOfRangeException(nameof(seconds));
        EnsureWritable();
        WriteParameter(NativeMethods.SetScreenSaverTimeout, (uint)seconds);
    }

    public void SetAutomatic(bool enabled)
    {
        EnsureWritable();
        if (enabled && ReadConfiguration().Path is null) throw new InvalidOperationException("NoSaverError");
        WriteParameter(NativeMethods.SetScreenSaverActive, enabled ? 1u : 0u);
    }

    public void SetScreenSaver(string? path)
    {
        EnsureWritable();
        if (path is not null && (!File.Exists(path) || !System.IO.Path.GetExtension(path).Equals(".scr", StringComparison.OrdinalIgnoreCase)))
            throw new FileNotFoundException("Screen saver is unavailable.", path);
        using var desktop = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", true);
        var previous = desktop.GetValue("SCRNSAVE.EXE") as string;
        if (path is null) desktop.DeleteValue("SCRNSAVE.EXE", false);
        else desktop.SetValue("SCRNSAVE.EXE", path, RegistryValueKind.String);
        // Like the Windows dialog, selecting None disables automatic startup.
        try { WriteParameter(NativeMethods.SetScreenSaverActive, path is null ? 0u : 1u); }
        catch
        {
            if (previous is null) desktop.DeleteValue("SCRNSAVE.EXE", false);
            else desktop.SetValue("SCRNSAVE.EXE", previous, RegistryValueKind.String);
            throw;
        }
    }

    private static void WriteParameter(uint action, uint value)
    {
        if (!NativeMethods.SetSystemParameter(action, value, 0, 0x01 | 0x02))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void OpenSettings() => Process.Start(new ProcessStartInfo
    {
        FileName = System.IO.Path.Combine(Environment.SystemDirectory, "control.exe"),
        Arguments = "desk.cpl,,@screensaver", UseShellExecute = true
    });

    public bool IsRunning() => ReadParameter(NativeMethods.GetScreenSaverRunning) != 0;

    public void RequestStart(nint window)
    {
        if (ReadConfiguration().Path is null)
            throw new InvalidOperationException("NoSaverError");
        if (!NativeMethods.PostMessage(window, NativeMethods.WmSysCommand, NativeMethods.ScScreenSave, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static int ReadParameter(uint action)
    {
        if (!NativeMethods.SystemParametersInfo(action, 0, out var value, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return value;
    }
}
