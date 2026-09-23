// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SharpEmu.Core.Loader;

/// <summary>
/// Stages a PS4/PS5 package into an application directory so the normal
/// SELF/ELF loader can consume it. Existing extracted applications are handled
/// in-process. A package key file is consulted only after a package backend
/// reports that protected content could not be processed.
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

        if (!PlayStationPackage.TryReadInfo(packagePath, out _))
        {
            message = "The file is not a recognized PS4/PS5 package.";
            return false;
        }

        // An already extracted application never needs package keys.
        if (PlayStationPackage.TryResolveExtractedApplication(
                packagePath,
                out ebootPath,
                out _))
        {
            message = "Using an existing extracted application; no package key was required.";
            return true;
        }

        var tool = FindPkgTool();
        if (tool is null)
        {
            message =
                "No PKG backend was found. If this package is already extracted, " +
                "launch its application directory/eboot.bin directly. A protected " +
                "retail PKG also requires an authorized package-processing backend.";
            return false;
        }

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
            var diagnostic = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;

            if (process.ExitCode != 0)
            {
                if (LooksLikeKeyFailure(diagnostic))
                {
                    return ExplainKeyRequirement(diagnostic);
                }

                message =
                    $"PkgTool failed with exit code {process.ExitCode}." +
                    (string.IsNullOrWhiteSpace(stderr) ? string.Empty : $" {stderr.Trim()}");
                return false;
            }

            if (TryFindEboot(outputDirectory, out ebootPath))
            {
                message =
                    $"PKG staged successfully with {Path.GetFileName(tool)}; no user package key was required.";
                return true;
            }

            if (LooksLikeKeyFailure(diagnostic))
            {
                return ExplainKeyRequirement(diagnostic);
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

    private static bool LooksLikeKeyFailure(string? diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic))
            return false;

        var text = diagnostic.ToLowerInvariant();
        return text.Contains("key") ||
               text.Contains("pfs") && (text.Contains("decrypt") || text.Contains("encrypted")) ||
               text.Contains("passcode");
    }

    private static bool ExplainKeyRequirement(string diagnostic)
    {
        var keyPath = Environment.GetEnvironmentVariable("NVDEMU_PKG_KEYS");
        if (string.IsNullOrWhiteSpace(keyPath))
            keyPath = PlayStationPackageKeyStore.DefaultPath;

        if (!PlayStationPackageKeyStore.TryLoad(keyPath, out var keys, out var keyMessage))
        {
            _lastMessage =
                $"[PKG][KEYS] The PKG backend reports protected/encrypted content. " +
                $"{keyMessage} Expected file: {keyPath}. " +
                "Use --create-pkg-key-file to create the user-owned template.";
            return false;
        }

        if (keys.Count == 0)
        {
            _lastMessage =
                $"[PKG][KEYS] The PKG backend reports protected/encrypted content, " +
                $"but '{keyPath}' contains no keys. Add only authorized key material and retry.";
            return false;
        }

        _lastMessage =
            $"[PKG][KEYS] The PKG backend reports protected/encrypted content and " +
            $"NVDEMU loaded {keys.Count} user-supplied key(s) from '{keyPath}'. " +
            "The configured backend must support those keys; NVDEMU does not bundle platform private keys.";
        return false;
    }

    private static string _lastMessage = string.Empty;

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