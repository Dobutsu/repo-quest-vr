// RepoQuestVk: for PerfLog on Pico 4 Ultra, whose Vulkan driver (512.772) allocates GPU memory in the kernel inside every
// vkCmdCopyImage and frees it in vkResetCommandPool (simpleperf, 18.10: 94% of the render thread). Idle until
// RQVK_Enable; then it wraps vkCmdCopyImage, vkResetCommandPool and vkCreateImage/vkDestroyImage
// through Unity's IUnityGraphicsVulkan::InterceptVulkanAPI and counts what is copied: per source/destination image
// (format, size, layers, usage when the image was created after RQVK_Enable; memory size otherwise), per region extent,
// with the time each call takes. RQVK_Take returns that since the last call as text. RQVK_SetMode changes what
// vkCmdCopyImage does, for PerfLog's A/B test: 0 copies, 1 does nothing (the picture may break: what the copies cost),
// 2 blits colour copies instead (vkCmdBlitImage, same pixels: whether the driver takes that path without the kernel).
// Also counted (RQVK_TakeMore): render passes begun, by size and load/store ops (each load or store of a tile-sized
// attachment costs bandwidth on Adreno); blits, resolves, buffer<->image copies and clears; GPU memory allocated and
// freed (the kernel again); queue submits and fence waits (the render thread waiting for the GPU); pipelines and shader
// modules created (a pipeline compiled mid-game is a hitch); pipeline barriers.
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

    // ---- RQVK_TakeMore: plain counters, and render passes by what they load and store

    struct Counter
    {
        std::atomic<long> count{0};
        std::atomic<long long> ns{0};
        std::atomic<long long> bytes{0};

        void Add(double ms, long long size = 0)
        {
            count++;
            ns += (long long)(ms * 1e6);
            bytes += size;
        }
    };

    enum
    {
        RenderPasses, Blits, Resolves, BufferToImage, ImageToBuffer, Clears, ClearAttachments, Allocations, Frees,
        Submits, Waits, Pipelines, PipelineCalls, Modules, Barriers, CounterCount
    };
    Counter counters[CounterCount];

    PFN_vkCmdBeginRenderPass realBeginPass = nullptr;
    PFN_vkCmdBeginRenderPass2 realBeginPass2 = nullptr;
    PFN_vkCreateRenderPass realCreatePass = nullptr;
    PFN_vkCreateRenderPass2 realCreatePass2 = nullptr;
    PFN_vkCmdBlitImage realBlit = nullptr;
    PFN_vkCmdResolveImage realResolve = nullptr;
    PFN_vkCmdCopyBufferToImage realBufferToImage = nullptr;
    PFN_vkCmdCopyImageToBuffer realImageToBuffer = nullptr;
    PFN_vkCmdClearColorImage realClearColor = nullptr;
    PFN_vkCmdClearDepthStencilImage realClearDepth = nullptr;
    PFN_vkCmdClearAttachments realClearAttachments = nullptr;
    PFN_vkAllocateMemory realAllocate = nullptr;
    PFN_vkFreeMemory realFree = nullptr;
    PFN_vkQueueSubmit realSubmit = nullptr;
    PFN_vkWaitForFences realWait = nullptr;
    PFN_vkCreateGraphicsPipelines realPipelines = nullptr;
    PFN_vkCreateShaderModule realModule = nullptr;
    PFN_vkCmdPipelineBarrier realBarrier = nullptr;

    std::unordered_map<VkRenderPass, std::string> passes;   // described at creation
    std::unordered_map<std::string, long> passesBegun;      // key: size and what the pass loads and stores
    std::unordered_map<std::string, Totals> blitted;        // key: what was blitted or resolved

    const char* LoadOp(VkAttachmentLoadOp op) { return op == VK_ATTACHMENT_LOAD_OP_LOAD ? "L" : op == VK_ATTACHMENT_LOAD_OP_CLEAR ? "C" : "-"; }
    const char* StoreOp(VkAttachmentStoreOp op) { return op == VK_ATTACHMENT_STORE_OP_STORE ? "S" : "-"; }

    template <typename Attachment>
    std::string DescribePass(uint32_t count, const Attachment* attachments, uint32_t multiview)
    {
        std::string s = multiview ? "mv " : "";
        for (uint32_t i = 0; i < count; i++)
        {
            const auto& a = attachments[i];
            char one[48];
            // Depth/stencil formats are 124-130; their stencil ops after a slash
            bool depth = a.format >= VK_FORMAT_D16_UNORM && a.format <= VK_FORMAT_D32_SFLOAT_S8_UINT;
            if (depth)
                snprintf(one, sizeof one, "%sf%d s%d %s%s/%s%s", i ? ", " : "", a.format, a.samples, LoadOp(a.loadOp),
                         StoreOp(a.storeOp), LoadOp(a.stencilLoadOp), StoreOp(a.stencilStoreOp));
            else
                snprintf(one, sizeof one, "%sf%d s%d %s%s", i ? ", " : "", a.format, a.samples, LoadOp(a.loadOp), StoreOp(a.storeOp));
            s += one;
        }
        return s;
    }

    VKAPI_ATTR VkResult VKAPI_CALL HookCreatePass(VkDevice device, const VkRenderPassCreateInfo* info,
                                                  const VkAllocationCallbacks* allocator, VkRenderPass* pass)
    {
        if (!realCreatePass)
            return VK_ERROR_INITIALIZATION_FAILED;
        auto result = realCreatePass(device, info, allocator, pass);
        if (result == VK_SUCCESS && pass && info)
        {
            uint32_t multiview = 0;
            for (auto next = (const VkBaseInStructure*)info->pNext; next; next = next->pNext)
                if (next->sType == VK_STRUCTURE_TYPE_RENDER_PASS_MULTIVIEW_CREATE_INFO)
                    multiview = ((const VkRenderPassMultiviewCreateInfo*)next)->subpassCount ? 1 : 0;
            auto text = DescribePass(info->attachmentCount, info->pAttachments, multiview);
            std::lock_guard<std::mutex> lock(gate);
            passes[*pass] = text;
        }
        return result;
    }

    VKAPI_ATTR VkResult VKAPI_CALL HookCreatePass2(VkDevice device, const VkRenderPassCreateInfo2* info,
                                                   const VkAllocationCallbacks* allocator, VkRenderPass* pass)
    {
        if (!realCreatePass2)
            return VK_ERROR_INITIALIZATION_FAILED;
        auto result = realCreatePass2(device, info, allocator, pass);
        if (result == VK_SUCCESS && pass && info)
        {
            uint32_t multiview = info->subpassCount > 0 && info->pSubpasses[0].viewMask ? 1 : 0;
            auto text = DescribePass(info->attachmentCount, info->pAttachments, multiview);
            std::lock_guard<std::mutex> lock(gate);
            passes[*pass] = text;
        }
        return result;
    }

    void CountPass(const VkRenderPassBeginInfo* info)
    {
        counters[RenderPasses].Add(0);
        if (!info)
            return;
        char size[32];
        snprintf(size, sizeof size, "%ux%u ", info->renderArea.extent.width, info->renderArea.extent.height);
        std::lock_guard<std::mutex> lock(gate);
        auto found = passes.find(info->renderPass);
        passesBegun[size + (found != passes.end() ? found->second : std::string("old"))]++;
    }

    VKAPI_ATTR void VKAPI_CALL HookBeginPass(VkCommandBuffer cb, const VkRenderPassBeginInfo* info, VkSubpassContents contents)
    {
        CountPass(info);
        if (realBeginPass)
            realBeginPass(cb, info, contents);
    }

    VKAPI_ATTR void VKAPI_CALL HookBeginPass2(VkCommandBuffer cb, const VkRenderPassBeginInfo* info, const VkSubpassBeginInfo* begin)
    {
        CountPass(info);
        if (realBeginPass2)
            realBeginPass2(cb, info, begin);
    }

    void CountBlit(const char* what, VkImage src, VkImage dst, double ms)
    {
        std::lock_guard<std::mutex> lock(gate);
        auto& t = blitted[std::string(what) + " " + ImageName(src) + " -> " + ImageName(dst)];
        t.count++;
        t.ms += ms;
    }

    VKAPI_ATTR void VKAPI_CALL HookBlit(VkCommandBuffer cb, VkImage src, VkImageLayout srcLayout, VkImage dst,
                                        VkImageLayout dstLayout, uint32_t count, const VkImageBlit* regions, VkFilter filter)
    {
        if (!realBlit)
            return;
        auto start = Now();
        realBlit(cb, src, srcLayout, dst, dstLayout, count, regions, filter);
        auto ms = Now() - start;
        counters[Blits].Add(ms);
        CountBlit("blit", src, dst, ms);
    }

    VKAPI_ATTR void VKAPI_CALL HookResolve(VkCommandBuffer cb, VkImage src, VkImageLayout srcLayout, VkImage dst,
                                           VkImageLayout dstLayout, uint32_t count, const VkImageResolve* regions)
    {
        if (!realResolve)
            return;
        auto start = Now();
        realResolve(cb, src, srcLayout, dst, dstLayout, count, regions);
        auto ms = Now() - start;
        counters[Resolves].Add(ms);
        CountBlit("resolve", src, dst, ms);
    }

    VKAPI_ATTR void VKAPI_CALL HookBufferToImage(VkCommandBuffer cb, VkBuffer src, VkImage dst, VkImageLayout layout,
                                                 uint32_t count, const VkBufferImageCopy* regions)
    {
        if (!realBufferToImage)
            return;
        auto start = Now();
        realBufferToImage(cb, src, dst, layout, count, regions);
        counters[BufferToImage].Add(Now() - start);
    }

    VKAPI_ATTR void VKAPI_CALL HookImageToBuffer(VkCommandBuffer cb, VkImage src, VkImageLayout layout, VkBuffer dst,
                                                 uint32_t count, const VkBufferImageCopy* regions)
    {
        if (!realImageToBuffer)
            return;
        auto start = Now();
        realImageToBuffer(cb, src, layout, dst, count, regions);
        counters[ImageToBuffer].Add(Now() - start);
    }

    VKAPI_ATTR void VKAPI_CALL HookClearColor(VkCommandBuffer cb, VkImage image, VkImageLayout layout, const VkClearColorValue* colour,
                                              uint32_t count, const VkImageSubresourceRange* ranges)
    {
        if (!realClearColor)
            return;
        auto start = Now();
        realClearColor(cb, image, layout, colour, count, ranges);
        counters[Clears].Add(Now() - start);
    }

    VKAPI_ATTR void VKAPI_CALL HookClearDepth(VkCommandBuffer cb, VkImage image, VkImageLayout layout,
                                              const VkClearDepthStencilValue* value, uint32_t count, const VkImageSubresourceRange* ranges)
    {
        if (!realClearDepth)
            return;
        auto start = Now();
        realClearDepth(cb, image, layout, value, count, ranges);
        counters[Clears].Add(Now() - start);
    }

    VKAPI_ATTR void VKAPI_CALL HookClearAttachments(VkCommandBuffer cb, uint32_t count, const VkClearAttachment* attachments,
                                                    uint32_t rects, const VkClearRect* rectList)
    {
        if (!realClearAttachments)
            return;
        auto start = Now();
        realClearAttachments(cb, count, attachments, rects, rectList);
        counters[ClearAttachments].Add(Now() - start);
    }

    VKAPI_ATTR VkResult VKAPI_CALL HookAllocate(VkDevice device, const VkMemoryAllocateInfo* info,
                                                const VkAllocationCallbacks* allocator, VkDeviceMemory* memory)
    {
        if (!realAllocate)
            return VK_ERROR_INITIALIZATION_FAILED;
        auto start = Now();
        auto result = realAllocate(device, info, allocator, memory);
        counters[Allocations].Add(Now() - start, info ? (long long)info->allocationSize : 0);
        return result;
    }

    VKAPI_ATTR void VKAPI_CALL HookFree(VkDevice device, VkDeviceMemory memory, const VkAllocationCallbacks* allocator)
    {
        if (!realFree)
            return;
        auto start = Now();
        realFree(device, memory, allocator);
        counters[Frees].Add(Now() - start);
    }

    VKAPI_ATTR VkResult VKAPI_CALL HookSubmit(VkQueue queue, uint32_t count, const VkSubmitInfo* submits, VkFence fence)
    {
        if (!realSubmit)
            return VK_ERROR_INITIALIZATION_FAILED;
        auto start = Now();
        auto result = realSubmit(queue, count, submits, fence);
        counters[Submits].Add(Now() - start);
        return result;
    }

    VKAPI_ATTR VkResult VKAPI_CALL HookWait(VkDevice device, uint32_t count, const VkFence* fences, VkBool32 all, uint64_t timeout)
    {
        if (!realWait)
            return VK_ERROR_INITIALIZATION_FAILED;
        auto start = Now();
        auto result = realWait(device, count, fences, all, timeout);
        counters[Waits].Add(Now() - start);
        return result;
    }

    VKAPI_ATTR VkResult VKAPI_CALL HookPipelines(VkDevice device, VkPipelineCache cache, uint32_t count,
                                                 const VkGraphicsPipelineCreateInfo* infos, const VkAllocationCallbacks* allocator,
                                                 VkPipeline* pipelines)
    {
        if (!realPipelines)
            return VK_ERROR_INITIALIZATION_FAILED;
        auto start = Now();
        auto result = realPipelines(device, cache, count, infos, allocator, pipelines);
        counters[PipelineCalls].Add(Now() - start);
        counters[Pipelines].count += count;
        return result;
    }

    VKAPI_ATTR VkResult VKAPI_CALL HookModule(VkDevice device, const VkShaderModuleCreateInfo* info,
                                              const VkAllocationCallbacks* allocator, VkShaderModule* module)
    {
        if (!realModule)
            return VK_ERROR_INITIALIZATION_FAILED;
        auto start = Now();
        auto result = realModule(device, info, allocator, module);
        counters[Modules].Add(Now() - start);
        return result;
    }

    VKAPI_ATTR void VKAPI_CALL HookBarrier(VkCommandBuffer cb, VkPipelineStageFlags srcStages, VkPipelineStageFlags dstStages,
                                           VkDependencyFlags flags, uint32_t memoryCount, const VkMemoryBarrier* memory,
                                           uint32_t bufferCount, const VkBufferMemoryBarrier* buffers, uint32_t imageCount,
                                           const VkImageMemoryBarrier* images)
    {
        counters[Barriers].Add(0);
        if (realBarrier)
            realBarrier(cb, srcStages, dstStages, flags, memoryCount, memory, bufferCount, buffers, imageCount, images);
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
    // The rest only count; each hook checks its real function, in case Unity does not know one of these names
    Intercept("vkCreateRenderPass", &HookCreatePass, realCreatePass);
    Intercept("vkCreateRenderPass2", &HookCreatePass2, realCreatePass2);
    Intercept("vkCmdBeginRenderPass", &HookBeginPass, realBeginPass);
    Intercept("vkCmdBeginRenderPass2", &HookBeginPass2, realBeginPass2);
    Intercept("vkCmdBlitImage", &HookBlit, realBlit);
    Intercept("vkCmdResolveImage", &HookResolve, realResolve);
    Intercept("vkCmdCopyBufferToImage", &HookBufferToImage, realBufferToImage);
    Intercept("vkCmdCopyImageToBuffer", &HookImageToBuffer, realImageToBuffer);
    Intercept("vkCmdClearColorImage", &HookClearColor, realClearColor);
    Intercept("vkCmdClearDepthStencilImage", &HookClearDepth, realClearDepth);
    Intercept("vkCmdClearAttachments", &HookClearAttachments, realClearAttachments);
    Intercept("vkAllocateMemory", &HookAllocate, realAllocate);
    Intercept("vkFreeMemory", &HookFree, realFree);
    Intercept("vkQueueSubmit", &HookSubmit, realSubmit);
    Intercept("vkWaitForFences", &HookWait, realWait);
    Intercept("vkCreateGraphicsPipelines", &HookPipelines, realPipelines);
    Intercept("vkCreateShaderModule", &HookModule, realModule);
    Intercept("vkCmdPipelineBarrier", &HookBarrier, realBarrier);
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

/// Since the last call: render passes (and the top kinds by count), blits/resolves (top by time), the other counters
extern "C" int UNITY_INTERFACE_EXPORT RQVK_TakeMore(char* buffer, int size, int top)
{
    long n[CounterCount];
    double ms[CounterCount];
    long long bytes[CounterCount];
    for (int i = 0; i < CounterCount; i++)
    {
        n[i] = counters[i].count.exchange(0);
        ms[i] = counters[i].ns.exchange(0) / 1e6;
        bytes[i] = counters[i].bytes.exchange(0);
    }
    std::vector<std::pair<std::string, long>> passList;
    std::vector<std::pair<std::string, Totals>> blitList;
    size_t known;
    {
        std::lock_guard<std::mutex> lock(gate);
        passList.assign(passesBegun.begin(), passesBegun.end());
        blitList.assign(blitted.begin(), blitted.end());
        passesBegun.clear();
        blitted.clear();
        known = passes.size();
    }
    char head[512];
    snprintf(head, sizeof head,
             "passes %ld (kinds known %zu), blits %ld in %.1f ms, resolves %ld in %.1f ms, buffer->image %ld in %.1f ms, "
             "image->buffer %ld in %.1f ms, clears %ld+%ld in %.1f ms, alloc %ld (%.1f MB) in %.1f ms, free %ld in %.1f ms, "
             "submits %ld in %.1f ms, fence waits %ld in %.1f ms, pipelines %ld in %.1f ms, shader modules %ld in %.1f ms, barriers %ld",
             n[RenderPasses], known, n[Blits], ms[Blits], n[Resolves], ms[Resolves], n[BufferToImage], ms[BufferToImage],
             n[ImageToBuffer], ms[ImageToBuffer], n[Clears], n[ClearAttachments], ms[Clears] + ms[ClearAttachments],
             n[Allocations], bytes[Allocations] / 1048576.0, ms[Allocations], n[Frees], ms[Frees], n[Submits], ms[Submits],
             n[Waits], ms[Waits], n[Pipelines], ms[PipelineCalls], n[Modules], ms[Modules], n[Barriers]);
    std::sort(passList.begin(), passList.end(), [](const auto& a, const auto& b) { return a.second > b.second; });
    std::sort(blitList.begin(), blitList.end(), [](const auto& a, const auto& b) { return a.second.ms > b.second.ms; });
    std::string text = head;
    for (int i = 0; i < top && i < (int)passList.size(); i++)
    {
        char line[32];
        snprintf(line, sizeof line, "\n    pass %ldx: ", passList[i].second);
        text += line + passList[i].first;
    }
    for (int i = 0; i < top && i < (int)blitList.size(); i++)
    {
        char line[64];
        snprintf(line, sizeof line, "\n    %ldx %.1f ms: ", blitList[i].second.count, blitList[i].second.ms);
        text += line + blitList[i].first;
    }
    if (size <= 0)
        return 0;
    auto length = std::min<int>((int)text.size(), size - 1);
    memcpy(buffer, text.data(), length);
    buffer[length] = 0;
    return length;
}
