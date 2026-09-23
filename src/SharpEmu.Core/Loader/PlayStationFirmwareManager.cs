// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Diagnostics;
using System.IO.Compression;

namespace SharpEmu.Core.Loader;

/// <summary>
/// Installs user-provided PS4 firmware modules.
/// A Sony PS4UPDATE.PUP is accepted as an input source, but NVDEMU does not
/// ship Sony keys or perform protected PUP decryption itself. When a PUP
/// contains protected firmware, a user-selected PUP backend can process it.
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
                InstallDirectory(source, ref installed, ref skipped);
            }
            else if (string.Equals(Path.GetExtension(source), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                InstallZip(source, ref installed, ref skipped);
            }
            else if (string.Equals(Path.GetExtension(source), ".sprx", StringComparison.OrdinalIgnoreCase))
            {
                if (InstallFile(source, out var alreadyInstalled))
                {
                    if (alreadyInstalled)
                        skipped++;
                    else
                        installed++;
                }
            }
            else if (string.Equals(Path.GetExtension(source), ".pup", StringComparison.OrdinalIgnoreCase))
            {
                if (!InstallPup(source, ref installed, ref skipped, out message))
                    return false;
            }
            else
            {
                message = "Firmware source must be a directory, .zip archive, .sprx file, or PS4 .PUP file.";
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

    private static void InstallDirectory(string source, ref int installed, ref int skipped)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*.sprx", SearchOption.AllDirectories))
        {
            if (!InstallFile(file, out var alreadyInstalled))
                continue;

            if (alreadyInstalled)
                skipped++;
            else
                installed++;
        }
    }

    private static void InstallZip(string source, ref int installed, ref int skipped)
    {
        using var archive = ZipFile.OpenRead(source);
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".sprx", StringComparison.OrdinalIgnoreCase))
                continue;

            var fileName = Path.GetFileName(entry.FullName);
            if (!IsKnownModuleName(fileName))
                continue;

            var destination = Path.Combine(DefaultDirectory, fileName);
            if (File.Exists(destination))
            {
                skipped++;
                continue;
            }

            entry.ExtractToFile(destination);
            if (!IsElfModule(destination))
            {
                File.Delete(destination);
                continue;
            }

            installed++;
        }
    }

    private static bool InstallPup(
        string source,
        ref int installed,
        ref int skipped,
        out string message)
    {
        if (!IsRecognizedPup(source, out var pupKind))
        {
            message = "The selected file has a .PUP extension but does not have a recognized PS4 PUP header.";
            return false;
        }

        Console.Error.WriteLine($"[FIRMWARE][PUP] Accepted PS4 firmware package ({pupKind}): {source}");

        var backend = FindPupBackend();
        if (backend is null)
        {
            message =
                "PS4 PUP accepted, but no PUP extraction backend is installed. " +
                "Set NVDEMU_PUPTOOL to a user-supplied PUP extraction tool that can process " +
                "your authorized firmware, then run --install-ps4-firmware again. " +
                "NVDEMU does not bundle Sony firmware keys or protected-PUP decryption.";
            return false;
        }

        var staging = Path.Combine(
            Path.GetTempPath(),
            "NVDEMU-pup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            var arguments = $"pup_extract {Quote(source)} {Quote(staging)}";
            var startInfo = new ProcessStartInfo
            {
                FileName = backend,
                Arguments = arguments,
                WorkingDirectory = staging,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                message = $"Could not start PUP backend: {backend}";
                return false;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                message =
                    $"PUP backend failed with exit code {process.ExitCode}. " +
                    $"{TrimDiagnostic(stderr, stdout)}";
                return false;
            }

            InstallDirectory(staging, ref installed, ref skipped);

            if (installed == 0 && skipped == 0)
            {
                message =
                    "PUP backend completed, but no valid libSce*.sprx modules were produced. " +
                    "The backend may have extracted protected firmware without decrypting the modules.";
                return false;
            }

            message =
                $"PUP processed successfully through the user-selected backend; " +
                $"installed {installed} firmware module(s), skipped {skipped}.";
            return true;
        }
        finally
        {
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static string? FindPupBackend()
    {
        var configured = Environment.GetEnvironmentVariable("NVDEMU_PUPTOOL");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);

        var names = OperatingSystem.IsWindows()
            ? new[] { "PupTool.exe", "PupTool.Core.exe", "PupTool" }
            : new[] { "PupTool", "PupTool.Core" };

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static bool IsRecognizedPup(string path, out string kind)
    {
        kind = "unknown";

        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[8];
            if (stream.Read(header) < 8)
                return false;

            // Sony PS4/PS5 update containers commonly use the SCEUF PUP header.
            if (header[0] == (byte)'S' &&
                header[1] == (byte)'C' &&
                header[2] == (byte)'E' &&
                header[3] == (byte)'U' &&
                header[4] == (byte)'F')
            {
                kind = "SCEUF";
                return true;
            }

            // PS4 download PUPs are commonly SLB2 containers holding encrypted
            // PS4UPDATE*.PUP fragments.
            if (header[0] == (byte)'S' &&
                header[1] == (byte)'L' &&
                header[2] == (byte)'B' &&
                header[3] == (byte)'2')
            {
                kind = "SLB2";
                return true;
            }
        }
        catch
        {
            // Invalid/unreadable PUP.
        }

        return false;
    }

    private static string TrimDiagnostic(string stderr, string stdout)
    {
        var diagnostic = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        diagnostic = diagnostic.Trim();
        return diagnostic.Length <= 600 ? diagnostic : diagnostic[..600];
    }

    private static string Quote(string value) =>
        """ + value.Replace("\", "\\").Replace(""", "\"") + """;

    private static bool InstallFile(string source, out bool alreadyInstalled)
    {
        alreadyInstalled = false;
        var fileName = Path.GetFileName(source);

        if (!IsKnownModuleName(fileName) || !IsElfModule(source))
            return false;

        var destination = Path.Combine(DefaultDirectory, fileName);
        if (File.Exists(destination))
        {
            alreadyInstalled = true;
            return true;
        }

        File.Copy(source, destination);
        return true;
    }

    private static bool IsKnownModuleName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.EndsWith(".sprx", StringComparison.OrdinalIgnoreCase))
            return false;

        return KnownModulePrefixes.Any(prefix =>
            fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsElfModule(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[4];
            return stream.Read(magic) == 4 &&
                   magic[0] == 0x7F &&
                   magic[1] == (byte)'E' &&
                   magic[2] == (byte)'L' &&
                   magic[3] == (byte)'F';
        }
        catch
        {
            return false;
        }
    }
}
