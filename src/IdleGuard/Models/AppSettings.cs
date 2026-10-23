namespace IdleGuard.Models;

public sealed record AppSettings(string Language = "zh-CN", bool UseSystemTimeout = false, int CustomTimeoutSeconds = 600, bool MonitoringEnabled = false,
    bool RunAsAdministrator = true, Dictionary<string, bool>? DeviceRules = null)
{
    public AppSettings Validated() => this with
    {
        Language = Language is "en-US" or "zh-CN" ? Language : "zh-CN",
        CustomTimeoutSeconds = CustomTimeoutSeconds is >= 1 and <= 86400 ? CustomTimeoutSeconds : 600
    };
}
