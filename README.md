<!--
SPDX-FileCopyrightText: 2026 NVDEMU
SPDX-License-Identifier: GPL-2.0-or-later
-->

# NVDS5

<p align="center">
  <img src="https://raw.githubusercontent.com/NVDEMU/nvd123-ps5/main/assets/images/logo_transparent.png" alt="NVDS5 logo" width="220">
</p>

<p align="center">
  <strong>An experimental PlayStation 5 emulator for Windows, Linux, and macOS.</strong>
</p>

<p align="center">
  Research-focused emulation project written in C#, with an emphasis on building the low-level infrastructure needed to run PlayStation 5 software.
</p>

---

## About

NVDS5 is an experimental PlayStation 5 emulator. The project is in active development and many parts of the PS5 system are still incomplete.

The goal is to progressively implement the system components required for games to boot, run, render graphics, handle input, and produce audio. Compatibility is expected to change frequently as new emulator functionality is implemented.

This project is intended for **research, education, and emulator development**.

## Current status

NVDS5 is still an early-stage emulator. Current development includes work across the CPU, kernel/system interfaces, executable loading, graphics, video output, game metadata, and GUI.

Depending on the game and emulator build, software may:

- Fail to boot
- Boot successfully
- Reach a menu
- Reach in-game execution
- Become playable

Compatibility results are tracked separately in the **[NVDS5 Compatibility List](index.html)**.

## Compatibility

### Current known compatibility

| Game | Status |
|---|---|
| **NEW Joe And Mac** | **Playable** |

The compatibility list is designed to grow as games are tested.

### Compatibility statuses

| Status | Meaning |
|---|---|
| **Doesn't Boot** | The game currently fails before reaching a usable boot state. |
| **Boots** | The game starts but does not currently reach a menu or playable game state. |
| **Menu** | The game reaches a usable menu, but gameplay is not currently working. |
| **Ingame** | The game reaches gameplay, but significant problems prevent it from being considered playable. |
| **Playable** | Gameplay is functional enough to play the game, although bugs or limitations may still exist. |

## Supported platforms

NVDS5 currently provides builds for:

- **Windows x64**
- **Linux x64**
- **macOS x64**

Apple Silicon Macs can use the macOS x64 build through Rosetta 2.

Platform support and graphics compatibility are still experimental.

## Download

Prebuilt releases are available from the repository's **[Releases](https://github.com/NVDEMU/nvd123-ps5/releases)** page.

Each release may provide packages for Windows, Linux, and macOS.

## Building from source

### Requirements

- .NET 10 SDK
- Git
- A supported Windows, Linux, or macOS development environment

Clone the repository and build the solution:

```bash
git clone https://github.com/NVDEMU/nvd123-ps5.git
cd nvd123-ps5
dotnet build
```

For a release build:

```bash
dotnet publish -c Release
```

Build output is placed under the repository's `artifacts` directory.

## Running games

NVDS5 is designed to work with legally obtained game data.

A typical workflow is:

1. Obtain your game data legally.
2. Build NVDS5 or download a release.
3. Launch the NVDS5 GUI.
4. Add the appropriate game directory or executable.
5. Select the game from the library.
6. Launch it and report the result if it does not work correctly.

Do **not** include copyrighted game files, PlayStation firmware, or other proprietary Sony assets in this repository.

## Reporting compatibility

If you test a game, please report:

- Game title
- Game version/update
- NVDS5 version or commit
- Host operating system
- CPU
- GPU
- Whether the game boots
- How far the game gets
- Important graphical, audio, input, or performance problems
- Relevant logs

The repository includes a game compatibility issue template to make reports easier to organize.

## Updating NVDS5

NVDS5 includes an update checker that checks the official NVDS5 GitHub repository for newer releases and commits.

Repository:

**https://github.com/NVDEMU/nvd123-ps5**

## Contributing

Contributions, testing, bug reports, compatibility reports, and development work are welcome.

Before making substantial changes, please read **[CONTRIBUTING.md](CONTRIBUTING.md)**.

When contributing:

- Keep changes focused.
- Test changes when possible.
- Include useful reproduction information for bugs.
- Avoid committing copyrighted game data or proprietary system files.
- Document compatibility changes when they affect game behavior.

## Legal and research disclaimer

NVDS5 is an independent emulator project.

The repository does not distribute PlayStation 5 firmware, copyrighted game data, or proprietary Sony assets.

Users are responsible for complying with the laws applicable to them and for obtaining any software or data they use with the emulator legally.

## License

NVDS5 is licensed under the **GNU General Public License v2.0 or later**.

See [LICENSE.txt](LICENSE.txt) for the full license text.

## Project links

- **Repository:** https://github.com/NVDEMU/nvd123-ps5
- **Releases:** https://github.com/NVDEMU/nvd123-ps5/releases
- **Compatibility List:** [index.html](index.html)
- **Issues:** https://github.com/NVDEMU/nvd123-ps5/issues
