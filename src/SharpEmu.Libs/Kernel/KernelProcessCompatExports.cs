// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Text;
using SharpEmu.HLE;

namespace SharpEmu.Libs.Kernel;

// PS4 process/module loader compatibility modeled after the public shadPS4
// kernel-process contract. The actual image mapping and initializer execution
// remain owned by SharpEmuRuntime/KernelModuleRegistry.
public static class KernelProcessCompatExports
{
    private const int MaxGuestPathLength = 1024;
    private const int MaxSymbolLength = 512;

    [SysAbiExport(
        Nid = "wzvqT4UqKX8",
        ExportName = "sceKernelLoadStartModule",
        Target = Generation.Gen4,
        LibraryName = "libKernel")]
    public static int KernelLoadStartModule(CpuContext ctx)
    {
        var pathAddress = ctx[CpuRegister.Rdi];
        var pResAddress = ctx[CpuRegister.R9];
        var flags = unchecked((uint)ctx[CpuRegister.Rcx]);

        if (pathAddress == 0 || flags != 0 ||
            !TryReadNullTerminatedUtf8(ctx, pathAddress, MaxGuestPathLength, out var guestPath) ||
            string.IsNullOrWhiteSpace(guestPath))
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        var modulePath = NormalizeModulePath(guestPath);
        if (string.IsNullOrWhiteSpace(modulePath))
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        var result = KernelModuleRegistry.LoadModule(modulePath);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine(
                $"[PS4][SYSMODULE] sceKernelLoadStartModule failed path='{guestPath}' " +
                $"error=0x{unchecked((uint)result.Error):X8}");
            return SetReturn(ctx, (OrbisGen2Result)result.Error);
        }

        if (pResAddress != 0 && !ctx.TryWriteUInt32(pResAddress, 0))
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        Console.Error.WriteLine(
            $"[PS4][SYSMODULE] sceKernelLoadStartModule path='{guestPath}' " +
            $"resolved='{modulePath}' handle={result.Handle}");
        return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_OK, unchecked((ulong)(uint)result.Handle));
    }

    [SysAbiExport(
        Nid = "LwG8g3niqwA",
        ExportName = "sceKernelDlsym",
        Target = Generation.Gen4,
        LibraryName = "libKernel")]
    public static int KernelDlsym(CpuContext ctx)
    {
        var handle = unchecked((int)ctx[CpuRegister.Rdi]);
        var symbolAddress = ctx[CpuRegister.Rsi];
        var outAddress = ctx[CpuRegister.Rdx];

        if (handle <= 0 || symbolAddress == 0 || outAddress == 0 ||
            !TryReadNullTerminatedUtf8(ctx, symbolAddress, MaxSymbolLength, out var symbolName) ||
            string.IsNullOrWhiteSpace(symbolName))
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        if (!KernelModuleRegistry.TryResolveModuleSymbol(handle, symbolName, out var address))
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_NOT_FOUND);
        }

        if (!ctx.TryWriteUInt64(outAddress, address))
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_OK);
    }

    [SysAbiExport(
        Nid = "959qrazPIrg",
        ExportName = "sceKernelGetProcParam",
        Target = Generation.Gen4,
        LibraryName = "libKernel")]
    public static int KernelGetProcParamCompat(CpuContext ctx)
    {
        return KernelRuntimeCompatExports.KernelGetProcParam(ctx);
    }

    private static string NormalizeModulePath(string guestPath)
    {
        var normalized = guestPath.Replace('\', '/').Trim();
        if (normalized.StartsWith("/app0/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("app0/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return normalized;
        }

        return "/app0/" + normalized;
    }

    private static bool TryReadNullTerminatedUtf8(
        CpuContext ctx,
        ulong address,
        int capacity,
        out string value)
    {
        var bytes = GC.AllocateUninitializedArray<byte>(capacity);
        for (var index = 0; index < bytes.Length; index++)
        {
            if (!ctx.Memory.TryRead(address + (ulong)index, bytes.AsSpan(index, 1)))
            {
                value = string.Empty;
                return false;
            }

            if (bytes[index] == 0)
            {
                value = Encoding.UTF8.GetString(bytes, 0, index);
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static int SetReturn(
        CpuContext ctx,
        OrbisGen2Result result,
        ulong? successValue = null)
    {
        ctx[CpuRegister.Rax] = successValue ?? unchecked((ulong)(int)result);
        return (int)result;
    }
}
