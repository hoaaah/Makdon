using Microsoft.Win32;

namespace Makdon;

public enum RegistryRoot { CurrentUser, LocalMachine }

/// <summary>
/// Seam baca/tulis registri untuk <see cref="FileAssociation"/>. Test memakai implementasi palsu di memori sehingga
/// registri sungguhan tidak tersentuh. Semua <c>subKey</c> relatif terhadap root, tanpa "\" di awal.
/// </summary>
public interface IRegistryStore
{
    bool KeyExists(RegistryRoot root, string subKey);

    /// <summary>Nilai string; null bila kunci/nilai tidak ada atau bukan string. Nama "" = nilai (Default).</summary>
    string? GetString(RegistryRoot root, string subKey, string name = "");

    IReadOnlyList<string> GetValueNames(RegistryRoot root, string subKey);
    IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string subKey);

    /// <summary>Menulis string (REG_SZ); kunci dibuat bila belum ada.</summary>
    void SetString(RegistryRoot root, string subKey, string name, string value);

    /// <summary>Menulis nilai kosong bertipe REG_NONE (dipakai OpenWithProgids); kunci dibuat bila belum ada.</summary>
    void SetEmptyValue(RegistryRoot root, string subKey, string name);

    /// <summary>Menghapus satu nilai; diam bila tidak ada.</summary>
    void DeleteValue(RegistryRoot root, string subKey, string name);

    /// <summary>Menghapus kunci beserta isinya; diam bila tidak ada.</summary>
    void DeleteKeyTree(RegistryRoot root, string subKey);
}

/// <summary>Implementasi nyata di atas Microsoft.Win32.Registry, selalu view 64-bit (kunci HKLM installer ada di view itu).</summary>
sealed class WindowsRegistryStore : IRegistryStore
{
    static RegistryKey Base(RegistryRoot root) =>
        RegistryKey.OpenBaseKey(root == RegistryRoot.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);

    public bool KeyExists(RegistryRoot root, string subKey)
    {
        using var baseKey = Base(root);
        using var key = baseKey.OpenSubKey(subKey);
        return key is not null;
    }

    public string? GetString(RegistryRoot root, string subKey, string name = "")
    {
        using var baseKey = Base(root);
        using var key = baseKey.OpenSubKey(subKey);
        return key?.GetValue(name, null) as string;
    }

    public IReadOnlyList<string> GetValueNames(RegistryRoot root, string subKey)
    {
        using var baseKey = Base(root);
        using var key = baseKey.OpenSubKey(subKey);
        return key?.GetValueNames() ?? [];
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string subKey)
    {
        using var baseKey = Base(root);
        using var key = baseKey.OpenSubKey(subKey);
        return key?.GetSubKeyNames() ?? [];
    }

    public void SetString(RegistryRoot root, string subKey, string name, string value)
    {
        using var baseKey = Base(root);
        using var key = baseKey.CreateSubKey(subKey);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void SetEmptyValue(RegistryRoot root, string subKey, string name)
    {
        using var baseKey = Base(root);
        using var key = baseKey.CreateSubKey(subKey);
        key.SetValue(name, Array.Empty<byte>(), RegistryValueKind.None);
    }

    public void DeleteValue(RegistryRoot root, string subKey, string name)
    {
        using var baseKey = Base(root);
        using var key = baseKey.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void DeleteKeyTree(RegistryRoot root, string subKey)
    {
        using var baseKey = Base(root);
        baseKey.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
    }
}
