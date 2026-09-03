using System.Reflection;

namespace Mixr_App;

public static class AppVersion
{
    static readonly Lazy<string> DisplayLazy = new(() =>
    {
        var asm = typeof(AppVersion).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // "1.2.3+abcdef" (SourceLink-Hash) → "1.2.3"
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    });

    /// <summary>SemVer wie im Release-Tag, z. B. „1.4.2“ oder „0.0.0-dev“.</summary>
    public static string Display => DisplayLazy.Value;
}
