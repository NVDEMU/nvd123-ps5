// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Libs.Gpu.Metal;
using SharpEmu.Libs.Gpu.Vulkan;

namespace SharpEmu.Libs.Gpu;

/// <summary>
/// Process-wide access point for the guest-GPU backend, mirroring HostPlatform for the
/// host seam: static HLE export classes resolve the renderer through <see cref="Current"/>.
/// Vulkan remains the default on Windows/Linux. macOS uses the native Metal backend
/// by default because it avoids the MoltenVK/Metal translation layer; set
/// SHARPEMU_GPU_BACKEND=vulkan to force the Vulkan path for diagnostics.
/// </summary>
internal static class GuestGpu
{
    private static readonly Lazy<IGuestGpuBackend> Instance = new(Create);

    public static IGuestGpuBackend Current => Instance.Value;

    private static IGuestGpuBackend Create()
    {
        var requested = Environment.GetEnvironmentVariable("SHARPEMU_GPU_BACKEND");
        if (string.IsNullOrEmpty(requested))
        {
            if (OperatingSystem.IsMacOS())
            {
                Console.Error.WriteLine("[LOADER][INFO] GPU backend: Metal (macOS default).");
                return new MetalGuestGpuBackend();
            }

            return new VulkanGuestGpuBackend();
        }

        if (requested.Equals("vulkan", StringComparison.OrdinalIgnoreCase))
        {
            return new VulkanGuestGpuBackend();
        }

        if (requested.Equals("metal", StringComparison.OrdinalIgnoreCase))
        {
            if (!OperatingSystem.IsMacOS())
            {
                Console.Error.WriteLine(
                    "[LOADER][WARN] SHARPEMU_GPU_BACKEND=metal is only available on macOS; using Vulkan.");
                return new VulkanGuestGpuBackend();
            }

            Console.Error.WriteLine("[LOADER][INFO] GPU backend: Metal (SHARPEMU_GPU_BACKEND).");
            return new MetalGuestGpuBackend();
        }

        Console.Error.WriteLine(
            $"[LOADER][WARN] Unknown SHARPEMU_GPU_BACKEND value '{requested}'; using Vulkan.");
        return new VulkanGuestGpuBackend();
    }
}
