namespace LyricPulse.App;

/// <summary>
/// Immutable CLI launch options parsed from <c>Program.Main(string[] args)</c>.
/// </summary>
public sealed record LaunchOptions(bool StartInDemoMode, bool RunSmokeTest)
{
    public static LaunchOptions Parse(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return new LaunchOptions(StartInDemoMode: false, RunSmokeTest: false);
        }

        var demo = false;
        var smoke = false;

        foreach (var arg in args)
        {
            if (string.Equals(arg, "--demo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-d", StringComparison.OrdinalIgnoreCase))
            {
                demo = true;
            }
            else if (string.Equals(arg, "--smoke-test", StringComparison.OrdinalIgnoreCase))
            {
                smoke = true;
            }
        }

        return new LaunchOptions(StartInDemoMode: demo, RunSmokeTest: smoke);
    }
}
