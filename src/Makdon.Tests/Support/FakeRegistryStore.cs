namespace Makdon.Tests.Support;

/// <summary>
/// <see cref="IRegistryStore"/> di memori untuk test <see cref="FileAssociation"/>: registri sungguhan tidak pernah disentuh.
/// Nama kunci dan nilai tidak peka huruf besar (seperti registri Windows); kunci induk ikut dibuat saat menulis.
/// </summary>
public sealed class FakeRegistryStore : IRegistryStore
{
    /// <summary>Satu nilai: <see cref="IsNone"/> = REG_NONE kosong (OpenWithProgids), selain itu REG_SZ.</summary>
    public sealed record Entry(string? Text, bool IsNone);

    sealed class Key(string name)
    {
        public string Name { get; } = name;
        public Dictionary<string, Entry> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Key> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    readonly Dictionary<RegistryRoot, Key> roots = new()
    {
        [RegistryRoot.CurrentUser] = new Key("HKCU"),
        [RegistryRoot.LocalMachine] = new Key("HKLM"),
    };

    /// <summary>Jumlah operasi tulis/hapus (Set*, Delete*) yang dipanggil kode yang diuji.</summary>
    public int Mutations { get; private set; }

    static string[] Split(string subKey)
    {
        Assert.False(subKey.StartsWith('\\'), "subKey tidak boleh diawali \\: " + subKey);
        return subKey.Split('\\', StringSplitOptions.RemoveEmptyEntries);
    }

    Key? Find(RegistryRoot root, string subKey)
    {
        var key = roots[root];
        foreach (var part in Split(subKey))
            if (!key.Children.TryGetValue(part, out key)) return null;
        return key;
    }

    Key Create(RegistryRoot root, string subKey)
    {
        var key = roots[root];
        foreach (var part in Split(subKey))
        {
            if (!key.Children.TryGetValue(part, out var child)) key.Children[part] = child = new Key(part);
            key = child;
        }
        return key;
    }

    public bool KeyExists(RegistryRoot root, string subKey) => Find(root, subKey) is not null;

    public string? GetString(RegistryRoot root, string subKey, string name = "") =>
        Find(root, subKey) is { } key && key.Values.TryGetValue(name, out var entry) && !entry.IsNone ? entry.Text : null;

    public IReadOnlyList<string> GetValueNames(RegistryRoot root, string subKey) => Find(root, subKey)?.Values.Keys.ToList() ?? [];

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string subKey) =>
        Find(root, subKey)?.Children.Values.Select(c => c.Name).ToList() ?? [];

    public void SetString(RegistryRoot root, string subKey, string name, string value)
    {
        Mutations++;
        Create(root, subKey).Values[name] = new Entry(value, IsNone: false);
    }

    public void SetEmptyValue(RegistryRoot root, string subKey, string name)
    {
        Mutations++;
        Create(root, subKey).Values[name] = new Entry(null, IsNone: true);
    }

    public void DeleteValue(RegistryRoot root, string subKey, string name)
    {
        Mutations++;
        Find(root, subKey)?.Values.Remove(name);
    }

    public void DeleteKeyTree(RegistryRoot root, string subKey)
    {
        Mutations++;
        var parts = Split(subKey);
        var parent = parts.Length == 1 ? roots[root] : Find(root, string.Join('\\', parts[..^1]));
        parent?.Children.Remove(parts[^1]);
    }

    // ---- Penyiapan oleh test (tidak dihitung sebagai mutasi) ----

    public FakeRegistryStore With(RegistryRoot root, string subKey, string name, string value)
    {
        Create(root, subKey).Values[name] = new Entry(value, IsNone: false);
        return this;
    }

    public FakeRegistryStore WithKey(RegistryRoot root, string subKey)
    {
        Create(root, subKey);
        return this;
    }

    /// <summary>
    /// Semua nilai di bawah root sebagai "kunci|nama" -> isi (REG_NONE ditulis "(none)"), kunci memakai ejaan saat dibuat.
    /// Kunci tanpa nilai tidak muncul; pakai <see cref="KeyExists"/> untuk itu.
    /// </summary>
    public SortedDictionary<string, string> Values(RegistryRoot root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Walk(Key key, string path)
        {
            foreach (var (name, entry) in key.Values) result[path + "|" + name] = entry.IsNone ? "(none)" : entry.Text ?? "";
            foreach (var child in key.Children.Values) Walk(child, path.Length == 0 ? child.Name : path + "\\" + child.Name);
        }
        Walk(roots[root], "");
        return result;
    }

    /// <summary>Semua path kunci di bawah root (untuk memeriksa bahwa kunci tertentu tidak pernah dibuat).</summary>
    public IReadOnlyList<string> AllKeys(RegistryRoot root)
    {
        var result = new List<string>();
        void Walk(Key key, string path)
        {
            foreach (var child in key.Children.Values)
            {
                var childPath = path.Length == 0 ? child.Name : path + "\\" + child.Name;
                result.Add(childPath);
                Walk(child, childPath);
            }
        }
        Walk(roots[root], "");
        return result;
    }
}
