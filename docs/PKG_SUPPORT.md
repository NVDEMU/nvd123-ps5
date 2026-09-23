# NVDEMU PKG support

NVDEMU recognizes PlayStation 4 `\\x7FCNT` packages and PlayStation 5 `\\x7FFIH` packages before launching them.

## User package keys

NVDEMU never ships platform-private package keys in the repository or release artifacts. If a future protected-package backend requires key material, the user can provide it locally.

Create the user key template:

    NVDEMU --create-pkg-key-file

The default file is:

- Windows: `%APPDATA%\\NVDEMU\\pkg-keys.json`
- macOS/Linux: the platform's .NET `ApplicationData` directory under `NVDEMU/pkg-keys.json`

A different file can be selected with:

    NVDEMU --pkg-keys /path/to/pkg-keys.json game.pkg

The file format is:

    {
      "version": 1,
      "keys": {
        "your-key-name": "00112233445566778899aabbccddeeff"
      }
    }

Only add key material that you are authorized to possess and use. NVDEMU does not print loaded key values to the log.

## Missing-key behavior

If a package cannot be resolved to an already-extracted application and no user key file is available, NVDEMU stops with a `[PKG][KEYS] Missing package keys` error instead of silently falling back to an unrelated extractor.

The current tree does **not** contain a retail PS4/PS5 DRM/key-unwrapping implementation. Public package metadata can be inspected in-process, and an already-extracted application can be launched directly. A protected retail package still requires a package backend capable of processing its protected PFS using authorized key material.

This separation is intentional: no Sony private keys are committed to the repository, and no secret key material is embedded in release binaries.