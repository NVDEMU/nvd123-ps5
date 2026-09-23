// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
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
            if (!TryValidateDecryptedApplication(ebootPath, out message))
            {
                ebootPath = string.Empty;
                return false;
            }

            message = "Using an existing extracted application; the application image is a valid decrypted ELF/fSELF.";
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
            if (!TryValidateDecryptedApplication(ebootPath, out message))
            {
                ebootPath = string.Empty;
                return false;
            }

            message = $"Using cached PKG staging directory: {outputDirectory}; the application image is a valid decrypted ELF/fSELF.";
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
                    message = ExplainKeyRequirement(diagnostic);
                    return false;
                }

                message =
                    $"PkgTool failed with exit code {process.ExitCode}." +
                    (string.IsNullOrWhiteSpace(stderr) ? string.Empty : $" {stderr.Trim()}");
                return false;
            }

            if (TryFindEboot(outputDirectory, out ebootPath))
            {
                if (!TryValidateDecryptedApplication(ebootPath, out message))
                {
                    ebootPath = string.Empty;
                    return false;
                }

                message =
                    $"PKG staged successfully with {Path.GetFileName(tool)}; the application image is a valid decrypted ELF/fSELF.";
                return true;
            }

            if (LooksLikeKeyFailure(diagnostic))
            {
                message = ExplainKeyRequirement(diagnostic);
                return false;
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

    /// <summary>
    /// Verifies that package processing produced an image that the normal
    /// SharpEmu loader can recognize as a decrypted bare ELF or a SELF whose
    /// embedded ELF header is readable. A PKG header alone is not sufficient.
    /// </summary>
    private static bool TryValidateDecryptedApplication(string ebootPath, out string message)
    {
        message = string.Empty;

        try
        {
            using var stream = new FileStream(
                ebootPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                0x1000,
                FileOptions.SequentialScan);

            if (stream.Length < 64)
            {
                message = $"PKG produced '{ebootPath}', but the application image is too small to be a valid ELF.";
                return false;
            }

            Span<byte> header = stackalloc byte[64];
            var read = stream.Read(header);
            if (read < header.Length)
            {
                message = $"PKG produced '{ebootPath}', but the application image header could not be read.";
                return false;
            }

            var magic = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
            var elfOffset = 0L;

            if (magic == 0x7F454C46)
            {
                elfOffset = 0;
            }
            else if (magic is 0x4F153D1D or 0x5414F5EE)
            {
                const int selfHeaderSize = 0x20;
                const int selfSegmentSize = 0x20;

                var segmentCount = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(0x18, 2));
                if (segmentCount == 0 || segmentCount > 0x4000)
                {
                    message = $"PKG produced '{ebootPath}', but its SELF segment table is invalid.";
                    return false;
                }

                // A retail encrypted/compressed SELF is not considered decrypted
                // merely because its embedded ELF header is visible. Reject any
                // blocked segment still carrying the encryption/compression flags.
                for (var i = 0; i < segmentCount; i++)
                {
                    var segmentOffset = selfHeaderSize + (i * selfSegmentSize);
                    if (segmentOffset + 8 > header.Length)
                    {
                        // The segment table extends beyond the first header read;
                        // read each descriptor directly from the stream below.
                        stream.Position = segmentOffset;
                        Span<byte> segmentHeader = stackalloc byte[8];
                        if (stream.Read(segmentHeader) < segmentHeader.Length)
                        {
                            message = $"PKG produced '{ebootPath}', but the SELF segment table is truncated.";
                            return false;
                        }

                        var segmentType = BinaryPrimitives.ReadUInt64LittleEndian(segmentHeader);
                        if ((segmentType & 0x800UL) != 0 && (segmentType & (0x2UL | 0x8UL)) != 0)
                        {
                            message =
                                $"PKG produced '{ebootPath}', but at least one SELF segment is still " +
                                "encrypted or compressed. Package decryption is incomplete.";
                            return false;
                        }
                    }
                }

                elfOffset = checked(selfHeaderSize + ((long)segmentCount * selfSegmentSize));
                if (elfOffset < 0 || elfOffset > stream.Length - 64)
                {
                    message = $"PKG produced '{ebootPath}', but its embedded ELF is outside the image.";
                    return false;
                }
            }
            else
            {
                message =
                    $"PKG produced '{ebootPath}', but the resulting image is still encrypted or " +
                    "otherwise not a loadable decrypted ELF/fSELF.";
                return false;
            }

            if (elfOffset != 0)
            {
                stream.Position = elfOffset;
                if (stream.Read(header) < header.Length)
                {
                    message = $"PKG produced '{ebootPath}', but the embedded ELF header could not be read.";
                    return false;
                }
            }

            if (BinaryPrimitives.ReadUInt32BigEndian(header[..4]) != 0x7F454C46 ||
                header[4] != 2 ||
                header[5] != 1 ||
                BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(18, 2)) != 62 ||
                BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(54, 2)) != 56)
            {
                message =
                    $"PKG produced '{ebootPath}', but its application image is not a valid decrypted " +
                    "x86-64 ELF/fSELF.";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or OverflowException
                or ArgumentOutOfRangeException)
        {
            message = $"Could not validate the decrypted application image '{ebootPath}': {exception.Message}";
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

    private static string ExplainKeyRequirement(string diagnostic)
    {
        var keyPath = Environment.GetEnvironmentVariable("NVDEMU_PKG_KEYS");
        if (string.IsNullOrWhiteSpace(keyPath))
            keyPath = PlayStationPackageKeyStore.DefaultPath;

        if (!PlayStationPackageKeyStore.TryLoad(keyPath, out var keys, out var keyMessage))
        {
            return $"[PKG][KEYS] The PKG backend reports protected/encrypted content. {keyMessage} Expected file: {keyPath}. Use --create-pkg-key-file to create the user-owned template.";
        }

        if (keys.Count == 0)
        {
            return $"[PKG][KEYS] The PKG backend reports protected/encrypted content, but '{keyPath}' contains no keys. Add only authorized key material and retry.";
        }

        return $"[PKG][KEYS] The PKG backend reports protected/encrypted content and NVDEMU loaded {keys.Count} user-supplied key(s) from '{keyPath}'. The configured backend must support those keys; NVDEMU does not bundle platform private keys.";
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