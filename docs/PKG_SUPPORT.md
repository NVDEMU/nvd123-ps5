<!--
SPDX-FileCopyrightText: 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# NVDEMU PKG and PS4 firmware support

NVDEMU recognizes PlayStation 4 `\\x7FCNT` packages and PlayStation 5 `\\x7FFIH` packages before launching them.

## PS4 firmware PUP input

The firmware installer accepts an official PS4 `PS4UPDATE.PUP` file:

    NVDEMU --install-ps4-firmware /path/to/PS4UPDATE.PUP

PUP handling is performed **inside NVDEMU**. There is no required `PupTool`, `NVDEMU_PUPTOOL` environment variable, or separately installed PUP extractor.

NVDEMU validates the PUP header and contains a built-in reader for the unencrypted SLB2 container layout. It extracts container entries into a temporary staging area and scans the extracted payloads for valid `libSce*.sprx` ELF firmware modules before installing them into `sys_modules`.

PS4 update packages can contain protected/encrypted firmware payloads. The PUP format documentation describes encrypted segment metadata and keys, and existing open-source PUP unpackers likewise distinguish container unpacking from the protected decryption step. citeturn1search0turn1search2 NVDEMU does not bundle Sony private keys or implement protected-PUP key recovery/decryption. Consequently, an untouched retail PUP may be recognized and parsed while still reporting that its protected payloads cannot be turned into loadable modules.

For user-owned firmware, NVDEMU also continues to accept already-decrypted firmware module directories, ZIP archives, and individual `.sprx` files. Emulator firmware modules are loaded from `sys_modules` at PS4 runtime; this matches the established LLE firmware-module model used by PS4 emulators. citeturn0search0

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
