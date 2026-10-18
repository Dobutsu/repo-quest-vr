using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace RepoQuest
{
    /// <summary>
    /// For PerfLog's A/B test: an OpenXR feature, added to the settings at startup while PerfLog runs (RepoXR starts XR
    /// later and keeps the features it finds), that asks the runtime for two extensions no feature of ours enables:
    /// XR_EXT_performance_settings, CPU and GPU level hints (Pico's runtime has it, and nothing sets the levels: Pico logs
    /// cpuLevel 0 gpuLevel 0), and XR_FB_display_refresh_rate (72 Hz instead of 90). Status says what came of it.
    /// </summary>
    public class PerfXr : OpenXRFeature
    {
        private const string PerfExtension = "XR_EXT_performance_settings";
        private const string RateExtension = "XR_FB_display_refresh_rate";

        // XrPerfSettingsDomainEXT, XrPerfSettingsLevelEXT
        public const int CpuDomain = 1, GpuDomain = 2;
        public const int SustainedLow = 25, SustainedHigh = 50, Boost = 75;

        public static PerfXr Instance { get; private set; }
        public static string Status { get; private set; } = "not added";

        private ulong session;
        private SetLevel setLevel;
        private RequestRate requestRate;
        private GetRate getRate;
        private EnumerateRates enumerateRates;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetInstanceProcAddr(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr function);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetLevel(ulong session, int domain, int level);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RequestRate(ulong session, float hz);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetRate(ulong session, out float hz);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int EnumerateRates(ulong session, uint capacity, out uint count, [Out] float[] rates);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (!PerfLog.Wanted())
                return;
            try
            {
                var settings = OpenXRSettings.Instance;
                var features = typeof(OpenXRSettings).GetField("features", BindingFlags.Instance | BindingFlags.NonPublic);
                if (settings == null || features == null)
                {
                    Status = "OpenXR settings not found";
                    return;
                }
                var feature = CreateInstance<PerfXr>();
                feature.name = "RepoQuest PerfXr";
                // Nothing but the settings array holds it; a scene change must not unload it
                feature.hideFlags = HideFlags.DontUnloadUnusedAsset;
                // What the editor copies from [OpenXRFeature] at build time
                Set(feature, "nameUi", "RepoQuest Perf Log");
                Set(feature, "version", "1");
                Set(feature, "company", "RepoQuest");
                Set(feature, "featureIdInternal", "io.repoxr.quest.perfxr");
                Set(feature, "openxrExtensionStrings", $"{PerfExtension} {RateExtension}");
                feature.enabled = true;
                var current = (OpenXRFeature[])features.GetValue(settings) ?? Array.Empty<OpenXRFeature>();
                features.SetValue(settings, current.Append(feature).ToArray());
                Instance = feature;
                Status = "added, no OpenXR instance yet";
            }
            catch (Exception e)
            {
                Status = $"not added: {e.GetType().Name}: {e.Message}";
            }
        }

        private static void Set(OpenXRFeature feature, string field, string value) =>
            typeof(OpenXRFeature).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(feature, value);

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            try
            {
                var get = Marshal.GetDelegateForFunctionPointer<GetInstanceProcAddr>(xrGetInstanceProcAddr);
                T Load<T>(string extension, string name) where T : Delegate =>
                    OpenXRRuntime.IsExtensionEnabled(extension) && get(xrInstance, name, out var function) == 0 &&
                    function != IntPtr.Zero
                        ? Marshal.GetDelegateForFunctionPointer<T>(function)
                        : null;
                setLevel = Load<SetLevel>(PerfExtension, "xrPerfSettingsSetPerformanceLevelEXT");
                requestRate = Load<RequestRate>(RateExtension, "xrRequestDisplayRefreshRateFB");
                getRate = Load<GetRate>(RateExtension, "xrGetDisplayRefreshRateFB");
                enumerateRates = Load<EnumerateRates>(RateExtension, "xrEnumerateDisplayRefreshRatesFB");
                Status = $"{PerfExtension} {(setLevel != null ? "on" : "missing")}, {RateExtension} {(requestRate != null ? "on" : "missing")}";
            }
            catch (Exception e)
            {
                Status = $"instance: {e.GetType().Name}: {e.Message}";
            }
            return true;
        }

        protected override void OnSessionCreate(ulong xrSession) => session = xrSession;

        protected override void OnSessionDestroy(ulong xrSession) => session = 0;

        /// <summary>Both CPU and GPU to one level; the XrResult of each (0 = success)</summary>
        public static string SetLevels(int level)
        {
            var self = Instance;
            if (self?.setLevel == null || self.session == 0)
                return $"cannot: {Status}";
            return $"cpu {self.setLevel(self.session, CpuDomain, level)}, gpu {self.setLevel(self.session, GpuDomain, level)}";
        }

        public static float RefreshRate()
        {
            var self = Instance;
            return self?.getRate != null && self.session != 0 && self.getRate(self.session, out var hz) == 0 ? hz : 0;
        }

        public static string RefreshRates()
        {
            var self = Instance;
            if (self?.enumerateRates == null || self.session == 0 || self.enumerateRates(self.session, 0, out var count, null) != 0)
                return "?";
            var rates = new float[count];
            return self.enumerateRates(self.session, count, out count, rates) == 0
                ? string.Join("/", rates.Take((int)count).Select(r => r.ToString("0.#")))
                : "?";
        }

        public static string RequestRefreshRate(float hz)
        {
            var self = Instance;
            if (self?.requestRate == null || self.session == 0)
                return $"cannot: {Status}";
            return $"result {self.requestRate(self.session, hz)}";
        }
    }
}
