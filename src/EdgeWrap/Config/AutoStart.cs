using Microsoft.Win32;

namespace EdgeWrap.Config;

/// <summary>
/// Manages the per-user "run at login" entry. When enabled from a self-contained
/// single-file exe that lives somewhere transient (Downloads, a build folder, …),
/// the exe first copies itself into a stable per-user location so auto-start keeps
/// working after the original is moved or deleted.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EdgeWrap";

    /// <summary>Stable per-user install directory for the single-file exe.</summary>
    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "Programs", "EdgeWrap");

    public static string InstallExePath => Path.Combine(InstallDir, "EdgeWrap.exe");

    private static string CurrentExe => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public static void Apply(bool enabled)
    {
        if (enabled)
            Enable();
        else
            Disable();
    }

    private static void Enable()
    {
        string exe = ResolveStableExe();
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{exe}\" --silent");
    }

    private static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// A path that will still exist at next login. If we are a single-file exe running
    /// from outside the canonical install dir, copy ourselves there and register that.
    /// Multi-file (dev / framework-dependent) builds need their sibling files, so they
    /// are registered in place.
    /// </summary>
    private static string ResolveStableExe()
    {
        string current = CurrentExe;

        if (string.Equals(current, InstallExePath, StringComparison.OrdinalIgnoreCase))
            return current; // already installed

        if (!IsSingleFileExe(current))
            return current; // can't safely relocate a multi-file build

        try
        {
            Directory.CreateDirectory(InstallDir);
            File.Copy(current, InstallExePath, overwrite: true);
            return InstallExePath;
        }
        catch
        {
            return current; // copy failed (e.g. locked) — fall back to the current path
        }
    }

    /// <summary>
    /// Heuristic: a single-file publish has no managed "EdgeWrap.dll" next to the exe,
    /// whereas Debug / framework-dependent builds do.
    /// </summary>
    private static bool IsSingleFileExe(string exePath)
    {
        string? dir = Path.GetDirectoryName(exePath);
        return dir is not null && !File.Exists(Path.Combine(dir, "EdgeWrap.dll"));
    }
}
