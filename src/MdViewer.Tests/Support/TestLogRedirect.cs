using System.Runtime.CompilerServices;

namespace MdViewer.Tests.Support;

/// <summary>Mengalihkan crash.log ke folder temp selama test supaya galat yang sengaja dipicu tidak mengotori log pengguna.</summary>
static class TestLogRedirect
{
    [ModuleInitializer]
    internal static void Init() =>
        CrashLog.LogPath = Path.Combine(Path.GetTempPath(), "MdViewer.Tests", "crash-" + Environment.ProcessId + ".log");
}
