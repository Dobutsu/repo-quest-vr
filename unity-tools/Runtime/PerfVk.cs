using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RepoQuest
{
    /// <summary>
    /// For PerfLog: the native RepoQuestVk (unity-tools/Native/RepoQuestVk.cpp), which counts vkCmdCopyImage and
    /// vkResetCommandPool and what they copy (on Pico 4 Ultra both spend the render thread in the kernel). Enable once;
    /// Take returns the counts since the last call; SetMode: 0 copy, 1 skip the copies, 2 blit colour copies instead.
    /// </summary>
    public static class PerfVk
    {
        public static string Status { get; private set; } = "off";
        private static bool on;
        private static readonly byte[] buffer = new byte[8192];

        public static void Enable()
        {
            try
            {
                var result = RQVK_Enable();
                on = result == 0;
                Status = result switch
                {
                    0 => "on",
                    1 => "no plugin interfaces (not loaded at startup)",
                    2 => "not Vulkan",
                    3 => "Vulkan not running",
                    _ => $"hooks failed ({result})",
                };
            }
            catch (Exception e)
            {
                Status = $"{e.GetType().Name}: {e.Message}";
            }
        }

        public static string Take(int top)
        {
            if (!on)
                return $"vk: {Status}";
            var n = RQVK_Take(buffer, buffer.Length, top);
            return "vk: " + Encoding.UTF8.GetString(buffer, 0, Math.Max(n, 0));
        }

        public const int Copy = 0, SkipCopies = 1, BlitCopies = 2;

        public static void SetMode(int value)
        {
            if (on)
                RQVK_SetMode(value);
        }

        [DllImport("RepoQuestVk")]
        private static extern int RQVK_Enable();

        [DllImport("RepoQuestVk")]
        private static extern int RQVK_Take(byte[] buffer, int size, int top);

        [DllImport("RepoQuestVk")]
        private static extern void RQVK_SetMode(int value);
    }
}
