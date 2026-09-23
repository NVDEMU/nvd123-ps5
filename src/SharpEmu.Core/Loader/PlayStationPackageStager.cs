// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SharpEmu.Core.Loader;

/// <summary>
/// Stages a PS4/PS5 package into an application directory so the normal
/// SELF/ELF loader can consume it. Existing extracted applications are
/// handled in-process. Protected packages may use a user-supplied key file
/// through NVDEMU_PKG_KEYS; NVDEMU never bundles proprietary platform keys.
/// </summary>
public static class PlayStationPackageStager
{
    public static bool TryStageApplication(
        string packagePath,
        out string ebootPath,
        out string message)
    {
        ebootPath = string.Empty;
        message = string.Empty;

        if (!PlayStationPackage.TryReadInfo(packagePath, out var info))
        {
            message = "The file is not a recognized PS4/PS5 package.";
            return false;
        }

        if (PlayStationPackage.TryResolveExtractedApplication(
                packagePath,
                out ebootPath,
                out _))
        {
            message = "Using an existing extracted application.";
            return true;
        }

        var keyPath = Environment.GetEnvironmentVariable("NVDEMU_PKG_KEYS");
        if (string.IsNullOrWhiteSpace(keyPath))
            keyPath = PlayStationPackageKeyStore.DefaultPath;

        if (!PlayStationPackageKeyStore.TryLoad(keyPath, out var keys, out var keyMessage))
        {
            message =
                $"[PKG][KEYS] Missing package keys. {keyMessage} " +
                $"Add your user-supplied keys with --create-pkg-key-file, " +
                $"then launch again. Expected file: {keyPath}";
            return false;
        }

        if (keys.Count == 0)
        {
            message =
                $"[PKG][KEYS] The package key file '{keyPath}' contains no keys. " +
                "Add the required user-supplied key material and retry.";
            return false;
        }

        // A protected retail package cannot be decrypted merely from its
        // public header. Keep the key material in a user-owned file so a
        // future in-process package backend can consume it without shipping
        // platform secrets in the emulator.
        message =
            $"[PKG][KEYS] Loaded {keys.Count} user-supplied key(s), but this " +
            "package requires a protected-PFS backend. No external extractor " +
            "is invoked by NVDEMU. Existing extracted applications can still " +
            "be launched directly.";
        return false;

        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NVDEMU",
            "pkg-cache");

        Directory.CreateDirectory(cacheRoot);

        var cacheKey = ComputeCacheKey(packagePath);
        var outputDirectory = Path.Combine(cacheRoot, cacheKey);

        if (Directory.Exists(outputDirectory) &&
            TryFindEboot(outputDirectory, out ebootPath))
        {
            message = $"Using cached PKG staging directory: {outputDirectory}";
            return true;
        }

        Directory.CreateDirectory(outputDirectory);

        var passcode = Environment.GetEnvironmentVariable("NVDEMU_PKG_PASSCODE");
        var arguments = new List<string> { "pkg_extract", "--verbose" };

        if (!string.IsNullOrWhiteSpace(passcode))
        {
            arguments.Add("--passcode");
            arguments.Add(passcode);
        }

        arguments.Add(packagePath);
        arguments.Add(outputDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = tool,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                message = "PkgTool could not be started.";
                return false;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            process.WaitForExit();

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                message =
                    $"PkgTool failed with exit code {process.ExitCode}." +
                    (string.IsNullOrWhiteSpace(stderr) ? string.Empty : $" {stderr.Trim()}");
                return false;
            }

            if (TryFindEboot(outputDirectory, out ebootPath))
            {
                message =
                    $"PKG staged successfully with {Path.GetFileName(tool)}.";
                return true;
            }

            message =
                "PkgTool completed, but no eboot.bin was found in the staged application." +
                (string.IsNullOrWhiteSpace(stdout) ? string.Empty : $" Output: {stdout.Trim()}");
            return false;
        }
        catch (Exception exception)
        {
            message = $"Unable to run PkgTool: {exception.Message}";
            return false;
        }
    }

    private static string? FindPkgTool()
    {
        var configured = Environment.GetEnvironmentVariable("NVDEMU_PKGTOOL");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);

        var names = OperatingSystem.IsWindows()
            ? new[] { "PkgTool.exe", "PkgTool.Core.exe", "PkgTool" }
            : new[] { "PkgTool", "PkgTool.Core" };

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
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

    private static bool TryFindEboot(string root, out string ebootPath)
    {
        ebootPath = string.Empty;

        var direct = new[]
        {
            Path.Combine(root, "eboot.bin"),
            Path.Combine(root, "app0", "eboot.bin"),
            Path.Combine(root, "app", "eboot.bin"),
        };

        foreach (var candidate in direct)
        {
            if (File.Exists(candidate))
            {
                ebootPath = candidate;
                return true;
            }
        }

        try
        {
            foreach (var candidate in Directory.EnumerateFiles(
                         root,
                         "eboot.bin",
                         SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, candidate);
                if (relative.Count(c => c == Path.DirectorySeparatorChar) <= 4)
                {
                    ebootPath = candidate;
                    return true;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }

    private static string ComputeCacheKey(string packagePath)
    {
        var file = new FileInfo(packagePath);
        var material =
            $"{Path.GetFullPath(packagePath)}\n{file.Length}\n{file.LastWriteTimeUtc.Ticks}";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
