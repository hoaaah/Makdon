namespace Makdon;

/// <summary>
/// Mutex bernama tetap yang dicari installer/uninstaller (AppMutex Inno) untuk meminta pengguna menutup Makdon dulu.
/// Hanya dibuat di mode terpasang, di luar <see cref="SingleInstance"/> (nama mutex itu memuat id pengguna). Dipegang
/// sepanjang umur proses; galat membuatnya diabaikan (installer tetap berjalan, hanya tanpa deteksi ini).
/// </summary>
static class InstallerMutex
{
    static readonly List<Mutex> Held = [];

    public static void Acquire(AppPaths paths)
    {
        if (paths.IsPortable || Held.Count > 0) return;
        foreach (var name in new[] { AppInfo.InstallerMutexName, AppInfo.InstallerGlobalMutexName })
        {
            try { Held.Add(new Mutex(false, name)); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException or ArgumentException)
            {
                // Mis. Global\ ditolak di sesi terbatas: bukan masalah bagi pengguna.
            }
        }
    }
}
