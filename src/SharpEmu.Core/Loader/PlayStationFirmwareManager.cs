// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.IO.Compression;

namespace SharpEmu.Core.Loader;

/// <summary>
/// Installs user-provided, already-dumped PS4 firmware modules.
/// NVDEMU never downloads, decrypts, or supplies Sony firmware.
/// </summary>
public static class PlayStationFirmwareManager
{
    private static readonly string[] KnownModulePrefixes =
    [
        "libSce",
    ];

    public static string DefaultDirectory =>
        Environment.GetEnvironmentVariable("SHARPEMU_SYS_MODULES") is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(AppContext.BaseDirectory, "sys_modules");

    public static IReadOnlyList<string> ListInstalledModules()
    {
        if (!Directory.Exists(DefaultDirectory))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(DefaultDirectory, "*.sprx", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    public static bool Install(
        string source,
        out int installed,
        out int skipped,
        out string message)
    {
        installed = 0;
        skipped = 0;

        if (!File.Exists(source) && !Directory.Exists(source))
        {
            message = $"Firmware source was not found: {source}";
            return false;
        }

        Directory.CreateDirectory(DefaultDirectory);

        try
        {
            if (Directory.Exists(source))
            {
                foreach (var file in Directory.EnumerateFiles(source, "*.sprx", SearchOption.AllDirectories))
                {
                    if (InstallFile(file, out var wasInstalled))
                    {
                        if (wasInstalled) installed++;
                        else skipped++;
                    }
                }
            }
            else if (string.Equals(Path.GetExtension(source), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.OpenRead(source);
                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.EndsWith(".sprx", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fileName = Path.GetFileName(entry.FullName);
                    if (string.IsNullOrWhiteSpace(fileName))
                        continue;

                    var destination = Path.Combine(DefaultDirectory, fileName);
                    if (File.Exists(destination))
                    {
                        skipped++;
                        continue;
                    }

                    entry.ExtractToFile(destination);
                    installed++;
                }
            }
            else if (string.Equals(Path.GetExtension(source), ".sprx", StringComparison.OrdinalIgnoreCase))
            {
                if (InstallFile(source, out var wasInstalled))
                {
                    if (wasInstalled) installed++;
                    else skipped++;
                }
            }
            else
            {
                message = "Firmware source must be a directory, .zip archive, or .sprx file.";
                return false;
            }

            message =
                $"Installed {installed} firmware module(s); skipped {skipped} already-installed module(s). " +
                $"Firmware directory: {DefaultDirectory}";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Firmware installation failed: {ex.Message}";
            return false;
        }
    }

    private static bool InstallFile(string source, out bool wasInstalled)
    {
        wasInstalled = false;
        var fileName = Path.GetFileName(source);

        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.EndsWith(".sprx", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!KnownModulePrefixes.Any(prefix =>
                fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return false;

        var destination = Path.Combine(DefaultDirectory, fileName);
        if (File.Exists(destination))
        {
            wasInstalled = true;
            return true;
        }

        File.Copy(source, destination);
        return true;
    }
}