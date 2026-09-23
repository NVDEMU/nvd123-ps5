// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Text.Json;

namespace SharpEmu.HLE.Host;

/// <summary>
/// Persistent host keyboard bindings shared by the launcher and emulator process.
/// Values are Windows virtual-key codes so the existing cross-platform input seam
/// can translate them consistently.
/// </summary>
public static class KeyboardBindings
{
    public const string Up = "Up";
    public const string Down = "Down";
    public const string Left = "Left";
    public const string Right = "Right";
    public const string Cross = "Cross";
    public const string Circle = "Circle";
    public const string Square = "Square";
    public const string Triangle = "Triangle";
    public const string L1 = "L1";
    public const string R1 = "R1";
    public const string L2 = "L2";
    public const string R2 = "R2";
    public const string L3 = "L3";
    public const string R3 = "R3";
    public const string Options = "Options";
    public const string Share = "Share";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        [Up] = 0x57, [Down] = 0x53, [Left] = 0x41, [Right] = 0x44,
        [Cross] = 0x5A, [Circle] = 0x58, [Square] = 0x43, [Triangle] = 0x56,
        [L1] = 0x51, [R1] = 0x45, [L2] = 0x52, [R2] = 0x46,
        [L3] = 0x10, [R3] = 0x11, [Options] = 0x09, [Share] = 0x20,
    };

    private static Dictionary<string, int> _bindings = new(Defaults, StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    public static IReadOnlyDictionary<string, int> Current
    {
        get
        {
            EnsureLoaded();
            lock (Gate) return new Dictionary<string, int>(_bindings, StringComparer.OrdinalIgnoreCase);
        }
    }

    public static int Get(string action)
    {
        EnsureLoaded();
        lock (Gate) return _bindings.TryGetValue(action, out var key) ? key : Defaults[action];
    }

    public static void Set(string action, int virtualKey)
    {
        if (!Defaults.ContainsKey(action)) return;
        lock (Gate)
        {
            _bindings[action] = virtualKey;
            _loaded = true;
        }
        Save();
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _bindings = new Dictionary<string, int>(Defaults, StringComparer.OrdinalIgnoreCase);
            _loaded = true;
        }
        Save();
    }

    public static void Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    _bindings = new Dictionary<string, int>(Defaults, StringComparer.OrdinalIgnoreCase);
                    _loaded = true;
                    return;
                }

                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                _bindings = new Dictionary<string, int>(Defaults, StringComparer.OrdinalIgnoreCase);
                if (loaded is not null)
                {
                    foreach (var pair in loaded)
                    {
                        if (_bindings.ContainsKey(pair.Key) && pair.Value is >= 0 and <= 0xFFFF)
                        {
                            _bindings[pair.Key] = pair.Value;
                        }
                    }
                }
            }
            catch
            {
                _bindings = new Dictionary<string, int>(Defaults, StringComparer.OrdinalIgnoreCase);
            }

            _loaded = true;
        }
    }

    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "keyboard-bindings.json");

    private static void EnsureLoaded()
    {
        if (!Volatile.Read(ref _loaded))
        {
            Load();
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppContext.BaseDirectory);
            lock (Gate)
            {
                File.WriteAllText(
                    SettingsPath,
                    JsonSerializer.Serialize(_bindings, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch
        {
            // Keyboard settings are best-effort.
        }
    }
}
