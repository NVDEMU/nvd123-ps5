// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;

namespace SharpEmu.Libs.TextToSpeech2;

public static class TextToSpeech2Exports
{
    [SysAbiExport(
        Nid = "UOjiprYwVNw",
        ExportName = "sceTextToSpeech2Initialize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceTextToSpeech2")]
    public static int Initialize(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "X0HZNbSiqyg",
        ExportName = "sceTextToSpeech2Open",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceTextToSpeech2")]
    public static int Open(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "2jiIxUmcsGo",
        ExportName = "sceTextToSpeech2Cancel",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceTextToSpeech2")]
    public static int Cancel(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }
}
