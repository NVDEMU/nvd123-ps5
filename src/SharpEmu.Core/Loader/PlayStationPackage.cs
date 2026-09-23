// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using System.Text;

namespace SharpEmu.Core.Loader;

public enum PlayStationPackageFormat
{
    Unknown,
    Ps4Cnt,
    Ps5Fih,
}

public sealed record PlayStationPackageInfo(
    PlayStationPackageFormat Format,
    string? ContentId,
    string? TitleId,
    long FileSize);

/// <summary>
/// Lightweight package detection/metadata reader used by the launcher.
/// It deliberately does not implement DRM/key extraction; the runtime still
/// consumes a decrypted ELF/SELF from an application filesystem.
/// </summary>
public static class PlayStationPackage
{
    private static ReadOnlySpan<byte> CntMagic => "\x7FCNT"u8;
    private static ReadOnlySpan<byte> FihMagic => "\x7FFIH"u8;

    public static bool TryReadInfo(string packagePath, out PlayStationPackageInfo info)
    {
        info = default!;
        if (!File.Exists(packagePath))
            return false;

        var length = new FileInfo(packagePath).Length;
        if (length < 4)
            return false;

        Span<byte> header = stackalloc byte[0x1000];
        var bytesRead = 0;
        using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 0x1000, FileOptions.SequentialScan))
        {
            bytesRead = stream.Read(header);
        }

        var data = header[..bytesRead];
        if (data.Length < 4)
            return false;

        PlayStationPackageFormat format;
        long cntOffset;

        if (data[..4].SequenceEqual(CntMagic))
        {
            format = PlayStationPackageFormat.Ps4Cnt;
            cntOffset = 0;
        }
        else if (data[..4].SequenceEqual(FihMagic))
        {
            format = PlayStationPackageFormat.Ps5Fih;
            if (data.Length < 0xA8)
                return false;

            cntOffset = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(data[0x58..0x60]));
            if (cntOffset < 0 || cntOffset > length - 4)
                return false;
        }
        else
        {
            return false;
        }

        string? contentId = null;
        if (cntOffset == 0)
        {
            if (data.Length >= 0x70)
                contentId = ReadAsciiZ(data[0x40..0x70]);
        }
        else
        {
            var required = checked(cntOffset + 0x70);
            if (required <= data.Length)
                contentId = ReadAsciiZ(data.Slice(checked((int)cntOffset + 0x40), 0x30));
            else
            {
                using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 0x1000, FileOptions.RandomAccess);
                stream.Position = cntOffset + 0x40;
                Span<byte> contentIdBytes = stackalloc byte[0x30];
                var n = stream.Read(contentIdBytes);
                if (n > 0)
                    contentId = ReadAsciiZ(contentIdBytes[..n]);
            }
        }

        var titleId = ExtractTitleId(contentId);
        info = new PlayStationPackageInfo(format, contentId, titleId, length);
        return true;
    }

    public static bool TryResolveExtractedApplication(
        string packagePath,
        out string ebootPath,
        out PlayStationPackageInfo? packageInfo)
    {
        ebootPath = string.Empty;
        packageInfo = null;

        if (!TryReadInfo(packagePath, out var info))
            return false;

        packageInfo = info;

        var fullPackagePath = Path.GetFullPath(packagePath);
        var directory = Path.GetDirectoryName(fullPackagePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(fullPackagePath);

        var roots = new[]
        {
            Path.Combine(directory, stem),
            Path.Combine(directory, stem + "-app0"),
            Path.Combine(directory, stem + "_app0"),
            Path.Combine(directory, "app0"),
            string.IsNullOrWhiteSpace(info.TitleId) ? string.Empty : Path.Combine(directory, info.TitleId),
            string.IsNullOrWhiteSpace(info.TitleId) ? string.Empty : Path.Combine(directory, info.TitleId + "-app0"),
            string.IsNullOrWhiteSpace(info.TitleId) ? string.Empty : Path.Combine(directory, info.TitleId + "_app0"),
            fullPackagePath + ".extracted",
        }
        .Where(Directory.Exists)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        foreach (var root in roots)
        {
            if (TryFindApplicationEboot(root, out ebootPath))
                return true;
        }

        return false;
    }

    private static bool TryFindApplicationEboot(string root, out string ebootPath)
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
            foreach (var candidate in Directory.EnumerateFiles(root, "eboot.bin", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, candidate);
                var depth = relative.Count(c => c == Path.DirectorySeparatorChar);
                if (depth <= 4)
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

    private static string? ReadAsciiZ(ReadOnlySpan<byte> bytes)
    {
        var length = bytes.IndexOf((byte)0);
        if (length < 0)
            length = bytes.Length;

        if (length == 0)
            return null;

        var value = Encoding.ASCII.GetString(bytes[..length]).Trim();
        return value.Length == 0 ? null : value;
    }

    private static string? ExtractTitleId(string? contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return null;

        var parts = contentId.Split('-', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if ((part.Length == 9 && (part.StartsWith("CUSA", StringComparison.OrdinalIgnoreCase) ||
                                      part.StartsWith("PPSA", StringComparison.OrdinalIgnoreCase))) ||
                (part.Length == 9 && part.StartsWith("PCSA", StringComparison.OrdinalIgnoreCase)))
            {
                return part.ToUpperInvariant();
            }
        }

        return null;
    }
}
