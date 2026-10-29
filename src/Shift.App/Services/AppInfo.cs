using System.Reflection;
namespace Shift.App.Services;

public static class AppInfo
{
    public static string Version { get; } = (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0-rc.6").Split('+')[0];
}
