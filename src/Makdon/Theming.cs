using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Makdon;

public enum AppThemeMode { System, Light, Dark }

/// <summary>
/// Pergantian tema: menukar ResourceDictionary Themes/Light.xaml atau Dark.xaml di Application.Resources.
/// Semua gaya memakai DynamicResource sehingga perubahan berlaku seketika.
/// </summary>
public static class ThemeManager
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string AppsUseLightThemeValue = "AppsUseLightTheme";
    const int DwmUseImmersiveDarkMode = 20;

    static ResourceDictionary? current;
    static bool subscribed;

    /// <summary>Dipicu setelah tema (atau hasil "Ikuti Sistem") berubah.</summary>
    public static event Action? ThemeChanged;

    public static AppThemeMode Mode { get; private set; } = AppThemeMode.System;

    /// <summary>true bila tema yang sedang aktif adalah Gelap.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Nilai tak dikenal/kosong menjadi <see cref="AppThemeMode.System"/>.</summary>
    public static AppThemeMode Parse(string? value) =>
        Enum.TryParse<AppThemeMode>(value, ignoreCase: true, out var mode) && Enum.IsDefined(mode) ? mode : AppThemeMode.System;

    /// <summary>Menentukan apakah tema gelap yang dipakai. Murni: systemUsesLight berasal dari <see cref="SystemUsesLightTheme"/>.</summary>
    public static bool ResolveIsDark(AppThemeMode mode, bool systemUsesLight) => mode switch
    {
        AppThemeMode.Dark => true,
        AppThemeMode.Light => false,
        _ => !systemUsesLight,
    };

    /// <summary>Membaca HKCU\...\Personalize\AppsUseLightTheme. Bila tak terbaca, dianggap terang.</summary>
    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(AppsUseLightThemeValue) is not int value || value != 0;
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Memuat kamus tema (dipakai juga untuk mencetak dengan warna terang).</summary>
    public static ResourceDictionary LoadDictionary(bool dark) =>
        new() { Source = new Uri($"pack://application:,,,/Makdon;component/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute) };

    public static void Apply(AppThemeMode mode)
    {
        Mode = mode;
        var dark = ResolveIsDark(mode, SystemUsesLightTheme());
        var app = Application.Current;
        if (app is null) return;

        if (!subscribed)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            subscribed = true;
        }

        var merged = app.Resources.MergedDictionaries;
        var replacement = LoadDictionary(dark);
        var index = current is not null ? merged.IndexOf(current) : -1;
        if (index < 0)
        {
            // Kamus tema bawaan dari App.xaml (Light) ikut diganti.
            index = merged.ToList().FindIndex(d => d.Source?.OriginalString.Contains("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true
                                                   || d.Source?.OriginalString.Contains("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true);
        }

        if (index >= 0) merged[index] = replacement;
        else merged.Insert(0, replacement);
        current = replacement;

        IsDark = dark;
        EditorTheme.ApplyMarkdownHighlighting();
        foreach (Window window in app.Windows) ApplyTitleBar(window);
        ThemeChanged?.Invoke();
    }

    /// <summary>Melepas langganan event sistem; panggil saat aplikasi keluar.</summary>
    public static void Shutdown()
    {
        if (!subscribed) return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        subscribed = false;
    }

    static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Mode != AppThemeMode.System || e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color)) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (Mode == AppThemeMode.System && ResolveIsDark(Mode, SystemUsesLightTheme()) != IsDark) Apply(Mode);
        });
    }

    /// <summary>Menyesuaikan title bar jendela (terang/gelap) dengan tema; diabaikan di Windows lama.</summary>
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var value = IsDark ? 1 : 0;
        try { _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int)); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

