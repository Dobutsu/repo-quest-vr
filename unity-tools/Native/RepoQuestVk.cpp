// RepoQuestVk: for PerfLog on Pico 4 Ultra, whose Vulkan driver (512.772) allocates GPU memory in the kernel inside every
// vkCmdCopyImage and frees it in vkResetCommandPool (simpleperf, 18.10: 94% of the render thread). Idle until
// RQVK_Enable; then it wraps vkCmdCopyImage, vkResetCommandPool and vkCreateImage/vkDestroyImage
// through Unity's IUnityGraphicsVulkan::InterceptVulkanAPI and counts what is copied: per source/destination image
// (format, size, layers, usage when the image was created after RQVK_Enable; memory size otherwise), per region extent,
// with the time each call takes. RQVK_Take returns that since the last call as text. RQVK_SetMode changes what
// vkCmdCopyImage does, for PerfLog's A/B test: 0 copies, 1 does nothing (the picture may break: what the copies cost),
// 2 blits colour copies instead (vkCmdBlitImage, same pixels: whether the driver takes that path without the kernel).
// Build: scripts/build-native.sh (NDK clang, arm64, static libc++).

#include <vulkan/vulkan.h>
#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

namespace
{
    IUnityInterfaces* interfaces = nullptr;
    IUnityGraphicsVulkanV2* vulkan = nullptr;
    std::atomic<bool> enabled{false};
    std::atomic<int> mode{0};

    PFN_vkCmdCopyImage realCopy = nullptr;
    PFN_vkResetCommandPool realReset = nullptr;
    PFN_vkCreateImage realCreate = nullptr;
    PFN_vkDestroyImage realDestroy = nullptr;
    PFN_vkGetImageMemoryRequirements memoryRequirements = nullptr;
    PFN_vkCmdBlitImage blit = nullptr;

    std::mutex gate;
    std::unordered_map<VkImage, std::string> images;   // described at creation
    struct Totals
    {
        long count = 0;
        double ms = 0;
    };
    std::unordered_map<std::string, Totals> copies;    // key: what was copied
    Totals copyTotal, resetTotal, skipped, blits;

    double Now()
    {
        return std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now().time_since_epoch()).count();
    }

    std::string Describe(const VkImageCreateInfo* info)
    {
        char s[160];
        snprintf(s, sizeof s, "fmt%d %ux%ux%u L%u m%u s%d %s u%x f%x", info->format, info->extent.width,
                 info->extent.height, info->extent.depth, info->arrayLayers, info->mipLevels, info->samples,
                 info->tiling == VK_IMAGE_TILING_OPTIMAL ? "opt" : "lin", info->usage, info->flags);
        return s;
    }

    // gate held
    std::string ImageName(VkImage image)
    {
        auto found = images.find(image);
        if (found != images.end())
            return found->second;
        VkDevice device = vulkan ? vulkan->Instance().device : VK_NULL_HANDLE;
        if (memoryRequirements && device)
        {
            VkMemoryRequirements req{};
            memoryRequirements(device, image, &req);
            char s[64];
            snprintf(s, sizeof s, "old %.1fMB", req.size / 1048576.0);
            return s;
        }
        return "old ?";
    }

    std::string Regions(uint32_t count, const VkImageCopy* regions)
    {
        if (count == 0)
            return "0 regions";
        const auto& r = regions[0];
        char s[128];
        snprintf(s, sizeof s, "%u reg %ux%ux%u L%u mip%u asp%x", count, r.extent.width, r.extent.height,
                 r.extent.depth, r.srcSubresource.layerCount, r.srcSubresource.mipLevel, r.srcSubresource.aspectMask);
        return s;
    }

    void Count(const std::string& key, double ms)
    {
        auto& t = copies[key];
        t.count++;
        t.ms += ms;
        copyTotal.count++;
        copyTotal.ms += ms;
    }

    VKAPI_ATTR void VKAPI_CALL HookCopy(VkCommandBuffer cb, VkImage src, VkImageLayout srcLayout, VkImage dst,
                                        VkImageLayout dstLayout, uint32_t count, const VkImageCopy* regions)
    {
        auto how = mode.load();
        if (how == 1)
        {
            std::lock_guard<std::mutex> lock(gate);
            skipped.count++;
            return;
        }
        if (how == 2 && blit && count > 0 && count <= 16)
        {
            bool colour = true;
            VkImageBlit converted[16];
            for (uint32_t i = 0; i < count; i++)
            {
                const auto& r = regions[i];
                colour = colour && r.srcSubresource.aspectMask == VK_IMAGE_ASPECT_COLOR_BIT;
                converted[i].srcSubresource = r.srcSubresource;
                converted[i].dstSubresource = r.dstSubresource;
                converted[i].srcOffsets[0] = r.srcOffset;
                converted[i].srcOffsets[1] = {r.srcOffset.x + (int32_t)r.extent.width, r.srcOffset.y + (int32_t)r.extent.height,
                                              r.srcOffset.z + (int32_t)r.extent.depth};
                converted[i].dstOffsets[0] = r.dstOffset;
                converted[i].dstOffsets[1] = {r.dstOffset.x + (int32_t)r.extent.width, r.dstOffset.y + (int32_t)r.extent.height,
                                              r.dstOffset.z + (int32_t)r.extent.depth};
            }
            if (colour)
            {
                auto start = Now();
                blit(cb, src, srcLayout, dst, dstLayout, count, converted, VK_FILTER_NEAREST);
                auto ms = Now() - start;
                std::lock_guard<std::mutex> lock(gate);
                blits.count++;
                blits.ms += ms;
                return;
            }
        }
        auto start = Now();
        realCopy(cb, src, srcLayout, dst, dstLayout, count, regions);
        auto ms = Now() - start;
        std::lock_guard<std::mutex> lock(gate);
        Count(ImageName(src) + " -> " + ImageName(dst) + " | " + Regions(count, regions), ms);
    }

    VKAPI_ATTR VkResult VKAPI_CALL HookReset(VkDevice device, VkCommandPool pool, VkCommandPoolResetFlags flags)
    {
        auto start = Now();
        auto result = realReset(device, pool, flags);
        auto ms = Now() - start;
        std::lock_guard<std::mutex> lock(gate);
        resetTotal.count++;
        resetTotal.ms += ms;
        return result;
    }

    VKAPI_ATTR VkResult VKAPI_CALL HookCreate(VkDevice device, const VkImageCreateInfo* info,
                                              const VkAllocationCallbacks* allocator, VkImage* image)
    {
        auto result = realCreate(device, info, allocator, image);
        if (result == VK_SUCCESS && image)
        {
            std::lock_guard<std::mutex> lock(gate);
            images[*image] = Describe(info);
        }
        return result;
    }

    VKAPI_ATTR void VKAPI_CALL HookDestroy(VkDevice device, VkImage image, const VkAllocationCallbacks* allocator)
    {
        {
            std::lock_guard<std::mutex> lock(gate);
            images.erase(image);
        }
        realDestroy(device, image, allocator);
    }

    template <typename T>
    void Intercept(const char* name, T hook, T& real)
    {
        real = reinterpret_cast<T>(vulkan->InterceptVulkanAPI(name, reinterpret_cast<PFN_vkVoidFunction>(hook)));
    }
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
    interfaces = unityInterfaces;
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {}

/// 0 hooked now (or already), 1 Unity's plugin interfaces never came, 2 not Vulkan, 3 Vulkan not running yet
extern "C" int UNITY_INTERFACE_EXPORT RQVK_Enable()
{
    if (enabled.load())
        return 0;
    if (!interfaces)
        return 1;
    vulkan = interfaces->Get<IUnityGraphicsVulkanV2>();
    auto graphics = interfaces->Get<IUnityGraphics>();
    if (!vulkan || !graphics || graphics->GetRenderer() != kUnityGfxRendererVulkan)
        return 2;
    auto instance = vulkan->Instance();
    if (!instance.device || !instance.getInstanceProcAddr)
        return 3;
    auto getDevice = reinterpret_cast<PFN_vkGetDeviceProcAddr>(instance.getInstanceProcAddr(instance.instance, "vkGetDeviceProcAddr"));
    if (getDevice)
    {
        memoryRequirements = reinterpret_cast<PFN_vkGetImageMemoryRequirements>(getDevice(instance.device, "vkGetImageMemoryRequirements"));
        blit = reinterpret_cast<PFN_vkCmdBlitImage>(getDevice(instance.device, "vkCmdBlitImage"));
    }
    Intercept("vkCreateImage", &HookCreate, realCreate);
    Intercept("vkDestroyImage", &HookDestroy, realDestroy);
    Intercept("vkResetCommandPool", &HookReset, realReset);
    // Unity 2022.3 has no vkCmdCopyImage2 (the driver calls it inside vkCmdCopyImage)
    Intercept("vkCmdCopyImage", &HookCopy, realCopy);
    enabled = realCopy && realReset && realCreate && realDestroy;
    return enabled.load() ? 0 : 4;
}

extern "C" void UNITY_INTERFACE_EXPORT RQVK_SetMode(int value)
{
    mode = value;
}

/// Since the last call: "copies N in X ms, resets N in X ms, skipped N" and the top keys by time, one a line
extern "C" int UNITY_INTERFACE_EXPORT RQVK_Take(char* buffer, int size, int top)
{
    std::vector<std::pair<std::string, Totals>> list;
    char head[160];
    {
        std::lock_guard<std::mutex> lock(gate);
        snprintf(head, sizeof head, "copies %ld in %.1f ms, blits %ld in %.1f ms, resets %ld in %.1f ms, skipped %ld, images known %zu",
                 copyTotal.count, copyTotal.ms, blits.count, blits.ms, resetTotal.count, resetTotal.ms, skipped.count,
                 images.size());
        list.assign(copies.begin(), copies.end());
        copies.clear();
        copyTotal = resetTotal = skipped = blits = Totals{};
    }
    std::sort(list.begin(), list.end(), [](const auto& a, const auto& b) { return a.second.ms > b.second.ms; });
    std::string text = head;
    for (int i = 0; i < top && i < (int)list.size(); i++)
    {
        char line[64];
        snprintf(line, sizeof line, "\n    %ldx %.1f ms: ", list[i].second.count, list[i].second.ms);
        text += line + list[i].first;
    }
    if (size <= 0)
        return 0;
    auto n = std::min<int>((int)text.size(), size - 1);
    memcpy(buffer, text.data(), n);
    buffer[n] = 0;
    return n;
}
