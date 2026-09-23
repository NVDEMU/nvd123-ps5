<!--
SPDX-FileCopyrightText: 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# NVDEMU PKG and PS4 firmware support

NVDEMU recognizes PlayStation 4 `\\x7FCNT` packages and PlayStation 5 `\\x7FFIH` packages before launching them.

## PS4 firmware PUP input

The firmware installer accepts an official PS4 `PS4UPDATE.PUP` file:

    NVDEMU --install-ps4-firmware /path/to/PS4UPDATE.PUP

The installer validates the PUP header and then looks for a user-selected PUP extraction backend. Set:

    NVDEMU_PUPTOOL=/path/to/PupTool

The backend is invoked as:

    PupTool pup_extract <input.pup> <output-directory>

Any resulting valid `libSce*.sprx` ELF modules are installed into NVDEMU's `sys_modules` directory.

This design lets users supply a Sony-distributed firmware PUP without making NVDEMU ship Sony firmware. NVDEMU does not contain Sony private keys or a protected-PUP decryption implementation. A backend must therefore perform any protected firmware processing using material the user is authorized to use.

A PUP can be accepted and identified even when no backend is installed; in that case NVDEMU reports exactly what is missing rather than treating the PUP as an ordinary ZIP file.

## Other firmware inputs

Individual already-decrypted modules and directories containing them remain supported:

    NVDEMU --install-ps4-firmware /path/to/sys_modules
    NVDEMU --install-ps4-firmware /path/to/libSceFont.sprx

ZIP archives containing already-decrypted `libSce*.sprx` modules are also supported.

List installed modules with:

    NVDEMU --list-ps4-firmware

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

If a package cannot be resolved to an already-extracted application and no user key file is available, NVDEMU stops with a `[PKG][KEYS]` error instead of silently falling back to an unrelated extractor.

The current tree does not contain a retail PS4/PS5 DRM/key-unwrapping implementation. Public package metadata can be inspected in-process, and an already-extracted application can be launched directly. A protected retail package still requires a package backend capable of processing its protected PFS using authorized key material.

This separation is intentional: no Sony private keys are committed to the repository, and no secret key material is embedded in release binaries.
