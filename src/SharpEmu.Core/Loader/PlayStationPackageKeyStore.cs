// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Text.Json;

namespace SharpEmu.Core.Loader;

/// <summary>
/// User-supplied package key storage. Keys are never bundled with NVDEMU.
/// The store only validates/loads user-provided values; package-specific
/// cryptographic operations remain responsible for deciding which key is needed.
/// </summary>
public static class PlayStationPackageKeyStore
{
    private const string DirectoryName = "NVDEMU";
    private const string FileName = "pkg-keys.json";

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            DirectoryName,
            FileName);

    public static bool TryLoad(
        string? path,
        out IReadOnlyDictionary<string, byte[]> keys,
        out string message)
    {
        keys = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        message = string.Empty;

        var effectivePath = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        if (!File.Exists(effectivePath))
        {
            message = $"No package key file was found at '{effectivePath}'.";
            return false;
        }

        try
        {
            using var stream = File.OpenRead(effectivePath);
            using var document = JsonDocument.Parse(stream);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                message = "The package key file must contain a JSON object.";
                return false;
            }

            var root = document.RootElement;
            if (!root.TryGetProperty("keys", out var keyObject) ||
                keyObject.ValueKind != JsonValueKind.Object)
            {
                message = "The package key file must contain a 'keys' object.";
                return false;
            }

            var parsed = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in keyObject.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name) ||
                    property.Value.ValueKind != JsonValueKind.String)
                {
                    message = $"Invalid package key entry '{property.Name}'.";
                    return false;
                }

                var value = property.Value.GetString() ?? string.Empty;
                if (!TryParseHex(value, out var bytes))
                {
                    message =
                        $"Package key '{property.Name}' is not valid hexadecimal.";
                    return false;
                }

                parsed[property.Name] = bytes;
            }

            keys = parsed;
            message = $"Loaded {parsed.Count} user-supplied package key(s).";
            return true;
        }
        catch (JsonException exception)
        {
            message = $"The package key file contains invalid JSON: {exception.Message}";
            return false;
        }
        catch (IOException exception)
        {
            message = $"Unable to read the package key file: {exception.Message}";
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            message = $"Unable to access the package key file: {exception.Message}";
            return false;
        }
    }

    public static string EnsureTemplate()
    {
        var directory = Path.GetDirectoryName(DefaultPath)!;
        Directory.CreateDirectory(directory);

        if (!File.Exists(DefaultPath))
        {
            File.WriteAllText(
                DefaultPath,
                "{\n  \"version\": 1,\n  \"keys\": {\n    \"example-key-name\": \"00112233445566778899aabbccddeeff\"\n  }\n}\n");
        }

        return DefaultPath;
    }

    private static bool TryParseHex(string value, out byte[] bytes)
    {
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            value = value[2..];

        if (value.Length == 0 || (value.Length & 1) != 0)
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }
}
