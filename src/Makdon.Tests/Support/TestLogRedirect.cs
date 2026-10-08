using System.Runtime.CompilerServices;

namespace Makdon.Tests.Support;

/// <summary>Mengalihkan crash.log ke folder temp selama test supaya galat yang sengaja dipicu tidak mengotori log pengguna.</summary>
static class TestLogRedirect
{
    [ModuleInitializer]
    internal static void Init() =>
        CrashLog.LogPath = Path.Combine(Path.GetTempPath(), "Makdon.Tests", "crash-" + Environment.ProcessId + ".log");
}
