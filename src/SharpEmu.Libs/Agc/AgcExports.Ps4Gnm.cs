// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Gpu;
using SharpEmu.Libs.Kernel;
using SharpEmu.Libs.VideoOut;

namespace SharpEmu.Libs.Agc;

// PS4 GNM compatibility bridge. The PS4 API exposes DCB/CCB arrays directly,
// while the existing renderer already consumes guest command streams. Keep the
// bridge HLE-only so PS5 AGC exports remain unchanged.
public static partial class AgcExports
{
    private static ulong _ps4GnmSubmissionSequence;

    internal static int SubmitPs4GnmCommandBuffers(
        CpuContext ctx,
        uint count,
        ulong dcbAddresses,
        ulong dcbSizes,
        ulong ccbAddresses,
        ulong ccbSizes)
    {
        if (count == 0)
        {
            ctx[CpuRegister.Rax] = 0;
            return (int)OrbisGen2Result.ORBIS_GEN2_OK;
        }

        if (count > 4096 || dcbAddresses == 0 || dcbSizes == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)(int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT;
        }

        (GuestGpu.Current as IGuestImageSnapshotBackend)?.AttachGuestMemory(ctx.Memory);

        for (uint i = 0; i < count; i++)
        {
            var dcbPointerAddress = dcbAddresses + ((ulong)i * sizeof(ulong));
            var sizeAddress = dcbSizes + ((ulong)i * sizeof(uint));
            if (!ctx.TryReadUInt64(dcbPointerAddress, out var commandAddress) ||
                !ctx.TryReadUInt32(sizeAddress, out var sizeBytes) ||
                commandAddress == 0 ||
                sizeBytes == 0 ||
                (sizeBytes & 3) != 0)
            {
                Console.Error.WriteLine(
                    $"[PS4][GNM] Invalid DCB[{i}] ptr=0x{dcbPointerAddress:X16} size=0x{sizeBytes:X}");
                ctx[CpuRegister.Rax] = unchecked((ulong)(int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
                return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT;
            }

            var ccbAddress = 0UL;
            var ccbSizeBytes = 0U;
            if (ccbAddresses != 0 && ccbSizes != 0)
            {
                _ = ctx.TryReadUInt64(ccbAddresses + ((ulong)i * sizeof(ulong)), out ccbAddress);
                _ = ctx.TryReadUInt32(ccbSizes + ((ulong)i * sizeof(uint)), out ccbSizeBytes);
            }

            var submissionId = unchecked(++_ps4GnmSubmissionSequence);
            if (!TrySubmitCommandStream(
                    ctx.Memory,
                    queue: 0,
                    commandAddress,
                    sizeBytes / sizeof(uint),
                    submissionId,
                    geometrySnapshots: null))
            {
                ctx[CpuRegister.Rax] = unchecked((ulong)(int)OrbisGen2Result.ORBIS_GEN2_ERROR_CANCELED);
                return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_CANCELED;
            }

            if (ccbAddress != 0 && ccbSizeBytes != 0 && (ccbSizeBytes & 3) == 0)
            {
                if (!TrySubmitCommandStream(
                        ctx.Memory,
                        queue: 1,
                        ccbAddress,
                        ccbSizeBytes / sizeof(uint),
                        submissionId,
                        geometrySnapshots: null))
                {
                    ctx[CpuRegister.Rax] = unchecked((ulong)(int)OrbisGen2Result.ORBIS_GEN2_ERROR_CANCELED);
                    return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_CANCELED;
                }
            }
        }

        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    internal static int SubmitPs4GnmAndFlip(
        CpuContext ctx,
        uint count,
        ulong dcbAddresses,
        ulong dcbSizes,
        ulong ccbAddresses,
        ulong ccbSizes,
        int videoOutHandle,
        int bufferIndex,
        int flipMode,
        long flipArgument)
    {
        var result = SubmitPs4GnmCommandBuffers(
            ctx,
            count,
            dcbAddresses,
            dcbSizes,
            ccbAddresses,
            ccbSizes);
        if (result != (int)OrbisGen2Result.ORBIS_GEN2_OK)
        {
            return result;
        }

        var oldRdi = ctx[CpuRegister.Rdi];
        var oldRsi = ctx[CpuRegister.Rsi];
        var oldRdx = ctx[CpuRegister.Rdx];
        var oldRcx = ctx[CpuRegister.Rcx];
        try
        {
            ctx[CpuRegister.Rdi] = unchecked((ulong)(uint)videoOutHandle);
            ctx[CpuRegister.Rsi] = unchecked((ulong)(uint)bufferIndex);
            ctx[CpuRegister.Rdx] = unchecked((ulong)(uint)flipMode);
            ctx[CpuRegister.Rcx] = unchecked((ulong)flipArgument);
            return VideoOutExports.VideoOutSubmitFlip(ctx);
        }
        finally
        {
            ctx[CpuRegister.Rdi] = oldRdi;
            ctx[CpuRegister.Rsi] = oldRsi;
            ctx[CpuRegister.Rdx] = oldRdx;
            ctx[CpuRegister.Rcx] = oldRcx;
        }
    }

    internal static int SubmitPs4GnmDone(CpuContext ctx)
    {
        var outcome = GuestGpu.Current.SubmitDone(ctx.Memory);
        var result = outcome == IdleOutcome.Completed
            ? OrbisGen2Result.ORBIS_GEN2_OK
            : OrbisGen2Result.ORBIS_GEN2_ERROR_CANCELED;
        ctx[CpuRegister.Rax] = unchecked((ulong)(int)result);
        return (int)result;
    }
}
