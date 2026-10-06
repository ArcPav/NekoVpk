using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace NekoVpk.Core;

public static class ThemeColor
{
    public const string DefaultPreviewHex = "#54A9FF";

    public const string SystemValue = "system";

    public const string AccentForegroundKey = "NekoAccentForeground";

    private static readonly string[] Keys =
    [
        "SemiColorPrimary",
        "SemiColorPrimaryPointerover",
        "SemiColorPrimaryActive",
        "SemiColorPrimaryDisabled",
        "SemiColorPrimaryLight",
        "SemiColorPrimaryLightPointerover",
        "SemiColorPrimaryLightActive",
        "SemiColorFocusBorder",
    ];

    private enum Role
    {
        OnPrimary,
        OnPrimaryPressed,
        AccentText,
    }

    private static readonly (string Key, Role Role)[] OverrideKeys =
    [
        ("ButtonSolidForeground", Role.OnPrimary),
        ("ButtonSolidPrimaryPressedForeground", Role.OnPrimaryPressed),
        ("ButtonDefaultPrimaryForeground", Role.AccentText),
        (AccentForegroundKey, Role.AccentText),
    ];

    private static readonly Dictionary<(ThemeVariant, string), Color> Originals = new();
    private static readonly Dictionary<(ThemeVariant, string), SolidColorBrush> Overrides = new();

    private static string? _setting;
    private static bool _subscribed;

    public static bool IsSystem(string? setting)
        => string.Equals(setting?.Trim(), SystemValue, StringComparison.OrdinalIgnoreCase);

    public static bool TryGetSystemAccent(out Rgb rgb)
    {
        rgb = default;
        try
        {
            var settings = Application.Current?.PlatformSettings;
            if (settings == null) return false;

            var accent = (Color)settings.GetColorValues().AccentColor1;
            rgb = new Rgb(accent.R, accent.G, accent.B);
            return true;
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex);
            return false;
        }
    }

    public static void Apply(string? setting)
    {
        var app = Application.Current;
        if (app == null) return;

        _setting = setting;

        bool custom;
        Rgb rgb;
        if (IsSystem(setting))
        {
            EnsureSubscribed(app);
            custom = TryGetSystemAccent(out rgb);
        }
        else
        {
            custom = Rgb.TryParse(setting, out rgb);
        }

        foreach (var (variant, dark) in new[] { (ThemeVariant.Dark, true), (ThemeVariant.Light, false) })
        {
            var shades = ThemeColorMath.Derive(rgb, dark);

            foreach (var key in Keys)
            {
                try
                {
                    if (!app.TryGetResource(key, variant, out var resource) || resource is not SolidColorBrush brush) continue;

                    if (!Originals.ContainsKey((variant, key))) Originals[(variant, key)] = brush.Color;

                    brush.Color = custom ? ToColor(Pick(key, shades, dark)) : Originals[(variant, key)];
                }
                catch (Exception ex)
                {
                    App.Logger.Error(ex);
                }
            }

            try
            {
                ApplyOverrides(app, variant, dark, custom, shades);
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex);
            }
        }
    }

    private static void EnsureSubscribed(Application app)
    {
        if (_subscribed) return;

        var settings = app.PlatformSettings;
        if (settings == null) return;

        settings.ColorValuesChanged += (_, _) =>
        {
            if (!IsSystem(_setting)) return;
            Dispatcher.UIThread.Post(() => Apply(_setting));
        };
        _subscribed = true;
    }

    private static void ApplyOverrides(Application app, ThemeVariant variant, bool dark, bool custom, ThemeColorMath.Shades shades)
    {
        var dictionary = GetThemeDictionary(app, variant);
        Originals.TryGetValue((variant, "SemiColorPrimary"), out var originalPrimary);

        foreach (var (key, role) in OverrideKeys)
        {
            if (!custom && role == Role.AccentText && !Originals.ContainsKey((variant, "SemiColorPrimary"))) continue;

            Color color = role switch
            {
                Role.OnPrimary => custom ? ToColor(ThemeColorMath.OnColor(shades.Primary)) : Colors.White,
                Role.OnPrimaryPressed => custom ? ToColor(ThemeColorMath.OnColor(shades.Active)) : Colors.White,
                _ => custom ? ToColor(ThemeColorMath.EnsureReadable(shades.Primary, dark)) : originalPrimary,
            };

            if (Overrides.TryGetValue((variant, key), out var brush))
            {
                brush.Color = color;
            }
            else
            {
                brush = new SolidColorBrush(color);
                Overrides[(variant, key)] = brush;
                dictionary[key] = brush;
            }
        }
    }

    private static ResourceDictionary GetThemeDictionary(Application app, ThemeVariant variant)
    {
        if (app.Resources.ThemeDictionaries.TryGetValue(variant, out var existing) && existing is ResourceDictionary dictionary)
            return dictionary;

        var created = new ResourceDictionary();
        app.Resources.ThemeDictionaries[variant] = created;
        return created;
    }

    private static Rgb Pick(string key, ThemeColorMath.Shades shades, bool dark) => key switch
    {
        "SemiColorPrimaryPointerover" => shades.Hover,
        "SemiColorPrimaryActive" => shades.Active,
        "SemiColorPrimaryDisabled" => shades.Disabled,
        "SemiColorPrimaryLight" => ThemeColorMath.LightTint(shades.Primary, dark, 0),
        "SemiColorPrimaryLightPointerover" => ThemeColorMath.LightTint(shades.Primary, dark, 1),
        "SemiColorPrimaryLightActive" => ThemeColorMath.LightTint(shades.Primary, dark, 2),
        "SemiColorFocusBorder" => ThemeColorMath.EnsureReadable(shades.Primary, dark),
        _ => shades.Primary,
    };

    private static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);
}
