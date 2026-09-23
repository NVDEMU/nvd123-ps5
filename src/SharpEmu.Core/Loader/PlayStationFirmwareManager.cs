// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.IO.Compression;
using System.Text;

namespace SharpEmu.Core.Loader;

/// <summary>
/// Installs user-provided PS4 firmware modules.
/// A Sony PS4UPDATE.PUP is accepted as an input source and is processed by
/// NVDEMU's built-in PUP container reader. Protected firmware payloads remain
/// subject to the platform cryptographic boundary and are not decrypted with
/// bundled Sony private keys.
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

        Console.Error.WriteLine(
            $"[FIRMWARE][PUP] Accepted PS4 firmware package ({pupKind}): {source}");

        var staging = Path.Combine(
            Path.GetTempPath(),
            "NVDEMU-pup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            if (!TryExtractPupContainer(
                    source,
                    staging,
                    out var extractedEntries,
                    out var protectedEntries,
                    out var error))
            {
                message =
                    $"NVDEMU could read the PUP container, but could not extract its firmware payloads: {error}";
                return false;
            }

            InstallDirectory(staging, ref installed, ref skipped);

            if (installed == 0 && skipped == 0)
            {
                if (protectedEntries > 0)
                {
                    message =
                        $"NVDEMU parsed {extractedEntries} PUP container entries, but {protectedEntries} " +
                        "payload(s) are protected. The emulator does not contain Sony private keys or a " +
                        "protected-PUP key-unwrapping implementation, so those payloads cannot be converted " +
                        "into loadable firmware modules from the protected retail PUP alone.";
                }
                else
                {
                    message =
                        $"NVDEMU parsed {extractedEntries} PUP container entries, but no valid " +
                        "libSce*.sprx ELF firmware modules were present.";
                }

                return false;
            }

            message =
                $"PUP processed by NVDEMU's built-in container reader; " +
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

    private static bool TryExtractPupContainer(
        string source,
        string outputDirectory,
        out int extractedEntries,
        out int protectedEntries,
        out string error)
    {
        extractedEntries = 0;
        protectedEntries = 0;
        error = string.Empty;

        try
        {
            using var stream = File.OpenRead(source);
            Span<byte> header = stackalloc byte[32];
            if (stream.Read(header) != header.Length)
            {
                error = "the file is smaller than an SLB2 header";
                return false;
            }

            if (header[0] != (byte)'S' ||
                header[1] != (byte)'L' ||
                header[2] != (byte)'B' ||
                header[3] != (byte)'2')
            {
                error =
                    "this PUP uses a protected SCEUF/fragment container; its payload metadata is not " +
                    "available to the built-in unencrypted SLB2 reader";
                protectedEntries = 1;
                return true;
            }

            var version = BitConverter.ToUInt32(header[4..8]);
            var entryCount = BitConverter.ToUInt32(header[12..16]);
            var sectorSize = BitConverter.ToUInt32(header[16..20]);

            if (sectorSize == 0)
                sectorSize = 0x200;

            if (sectorSize > 0x10000)
            {
                error = $"invalid SLB2 sector size 0x{sectorSize:X}";
                return false;
            }

            if (entryCount == 0 || entryCount > 10000)
            {
                error = $"invalid SLB2 entry count {entryCount}";
                return false;
            }

            Console.Error.WriteLine(
                $"[FIRMWARE][PUP] Built-in SLB2 reader: version={version}, entries={entryCount}, sectorSize=0x{sectorSize:X}");

            const int entrySize = 48;
            var tableSize = checked((int)entryCount * entrySize);
            var table = new byte[tableSize];
            stream.ReadExactly(table);

            for (var index = 0; index < entryCount; index++)
            {
                var entryOffset = checked((int)index * entrySize);
                var sectorOffset = BitConverter.ToUInt32(table, entryOffset);
                var fileSize = BitConverter.ToUInt32(table, entryOffset + 4);
                var nameBytes = table.AsSpan(entryOffset + 16, 32);
                var nameLength = nameBytes.IndexOf((byte)0);
                if (nameLength < 0)
                    nameLength = nameBytes.Length;

                var name = Encoding.ASCII.GetString(nameBytes[..nameLength]).Trim();
                if (string.IsNullOrWhiteSpace(name))
                    name = $"entry-{index:D5}.bin";

                var dataOffset = checked((long)sectorOffset * sectorSize);
                if (dataOffset < 0 ||
                    dataOffset > stream.Length ||
                    fileSize > stream.Length - dataOffset)
                {
                    protectedEntries++;
                    continue;
                }

                var safeName = Path.GetFileName(name);
                if (string.IsNullOrWhiteSpace(safeName))
                {
                    protectedEntries++;
                    continue;
                }

                var destination = Path.Combine(outputDirectory, $"{index:D5}-{safeName}");
                stream.Position = dataOffset;
                using var output = File.Create(destination);
                CopyExactly(stream, output, fileSize);
                extractedEntries++;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void CopyExactly(Stream input, Stream output, uint length)
    {
        var buffer = new byte[64 * 1024];
        var remaining = (long)length;

        while (remaining > 0)
        {
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = input.Read(buffer, 0, requested);
            if (read <= 0)
                throw new EndOfStreamException("Unexpected end of PUP entry.");

            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

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
