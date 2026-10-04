"""Apply source edits to the embedded package sources of the VR project.

HarmonyBaker can only bake patches into assemblies the project ships precompiled. Input System and uGUI are compiled
from source in the Quest project, so the three RepoXR patches on them are applied here as source edits instead
(the Quest build always runs in VR, so they are unconditional, like RepoXR's VR patch set):
  - XRPatches.OnBeforeInitialize: XRSupport.Initialize runs once (RepoXR re-initializes the Input System plugins)
  - InputPatches.AllowBackgroundTracking: InputSettings.backgroundBehavior is always IgnoreFocus
  - InputPatches.PreventDoublePressPatch: Button.OnPointerClick does nothing on the game's MenuButtons
Post Processing v2 gets the fixes it needs for single-pass instanced (multiview) stereo on the Quest:
  - it only recognises the eye texture array as single-pass instanced on desktop and consoles; on Android it took it
    for a double-wide texture and the left eye came out white
  - its Vulkan shaders have no STEREO_INSTANCING_ENABLED variant (the vertex shader cannot pick the eye's layer), so
    each eye's result is drawn into that eye's slice alone, with the camera's stereo mode off meanwhile
Usage: python scripts/patch_packages.py <unity project>
"""

import sys
from pathlib import Path

MARKER = "RepoXR (Quest)"

PATCHES = [
    ("com.unity.inputsystem/InputSystem/Plugins/XR/XRSupport.cs",
     """        public static void Initialize()
        {
""",
     f"""        private static bool s_RepoXRInitialized;

        public static void Initialize()
        {{
            // {MARKER}: XRPatches.OnBeforeInitialize. On PC that prefix only lands after the Input System has started,
            // so it skips the second run (RepoXR's Plugin.Awake calls PerformDefaultPluginInitialization again); the
            // first one registers XRHMD/XRController, which OpenXR's device layouts derive from
            if (s_RepoXRInitialized)
                return;
            s_RepoXRInitialized = true;
"""),
    ("com.unity.inputsystem/InputSystem/InputSettings.cs",
     """            set
            {
                if (m_BackgroundBehavior == value)
                    return;
                m_BackgroundBehavior = value;
""",
     f"""            set
            {{
                // {MARKER}: InputPatches.AllowBackgroundTracking
                value = BackgroundBehavior.IgnoreFocus;
                if (m_BackgroundBehavior == value)
                    return;
                m_BackgroundBehavior = value;
"""),
    ("com.unity.ugui/Runtime/UI/Core/Button.cs",
     """        public virtual void OnPointerClick(PointerEventData eventData)
        {
""",
     f"""        public virtual void OnPointerClick(PointerEventData eventData)
        {{
            // {MARKER}: InputPatches.PreventDoublePressPatch; the game handles MenuButton clicks itself
            if (GetComponent("MenuButton") != null)
                return;

"""),
    ("com.unity.postprocessing/PostProcessing/Runtime/PostProcessRenderContext.cs",
     """#if UNITY_STANDALONE || UNITY_EDITOR || UNITY_PS4 || UNITY_PS5
                    if (xrDesc.dimension == TextureDimension.Tex2DArray)
""",
     f"""                    // {MARKER}: the Quest renders both eyes into one texture array too (single-pass instanced /
                    // multiview); without Android here the array was taken for a double-wide texture
#if UNITY_STANDALONE || UNITY_EDITOR || UNITY_PS4 || UNITY_PS5 || UNITY_ANDROID
                    if (xrDesc.dimension == TextureDimension.Tex2DArray)
"""),
    ("com.unity.postprocessing/PostProcessing/Runtime/Utils/RuntimeUtilities.cs",
     """        public static void BlitFullscreenTriangleToTexArray(this CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, PropertySheet propertySheet, int pass, bool clear = false, int depthSlice = -1)
        {
            cmd.SetGlobalTexture(ShaderIDs.MainTex, source);
            cmd.SetGlobalFloat(ShaderIDs.DepthSlice, depthSlice);
            cmd.SetRenderTarget(destination, 0, CubemapFace.Unknown, -1);
""",
     f"""        public static void BlitFullscreenTriangleToTexArray(this CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, PropertySheet propertySheet, int pass, bool clear = false, int depthSlice = -1)
        {{
            cmd.SetGlobalTexture(ShaderIDs.MainTex, source);
            cmd.SetGlobalFloat(ShaderIDs.DepthSlice, depthSlice);
#if UNITY_ANDROID
            // {MARKER}: the Vulkan shaders cannot select the layer from the vertex shader (no
            // STEREO_INSTANCING_ENABLED variant), so bind the eye's slice alone and draw normally
            cmd.SetRenderTarget(destination, 0, CubemapFace.Unknown, depthSlice);
#else
            cmd.SetRenderTarget(destination, 0, CubemapFace.Unknown, -1);
#endif
"""),
    ("com.unity.postprocessing/PostProcessing/Runtime/PostProcessLayer.cs",
     """#if UNITY_2019_1_OR_NEWER
            if (context.stereoActive && context.numberOfEyes > 1 && context.stereoRenderingMode == PostProcessRenderContext.StereoRenderingMode.SinglePass)
            {
                cmd.SetSinglePassStereo(SinglePassStereoMode.None);
                cmd.DisableShaderKeyword("UNITY_SINGLE_PASS_STEREO");
            }
#endif
""",
     f"""#if UNITY_2019_1_OR_NEWER
            if (context.stereoActive && context.numberOfEyes > 1 && context.stereoRenderingMode == PostProcessRenderContext.StereoRenderingMode.SinglePass)
            {{
                cmd.SetSinglePassStereo(SinglePassStereoMode.None);
                cmd.DisableShaderKeyword("UNITY_SINGLE_PASS_STEREO");
            }}
#endif
#if UNITY_ANDROID
            // {MARKER}: the stack runs once per eye into single-layer targets (one eye's slice or a 2D
            // temporary); with the camera's multiview still on, every draw would be broadcast to both layers
            if (context.stereoActive && context.numberOfEyes > 1 && context.stereoRenderingMode == PostProcessRenderContext.StereoRenderingMode.SinglePassInstanced)
                cmd.SetSinglePassStereo(SinglePassStereoMode.None);
#endif
"""),
    ("com.unity.postprocessing/PostProcessing/Runtime/PostProcessLayer.cs",
     """#if UNITY_2019_1_OR_NEWER
            if (context.stereoActive && context.numberOfEyes > 1 && context.stereoRenderingMode == PostProcessRenderContext.StereoRenderingMode.SinglePass)
            {
                cmd.SetSinglePassStereo(SinglePassStereoMode.SideBySide);
                cmd.EnableShaderKeyword("UNITY_SINGLE_PASS_STEREO");
            }
#endif
""",
     f"""#if UNITY_2019_1_OR_NEWER
            if (context.stereoActive && context.numberOfEyes > 1 && context.stereoRenderingMode == PostProcessRenderContext.StereoRenderingMode.SinglePass)
            {{
                cmd.SetSinglePassStereo(SinglePassStereoMode.SideBySide);
                cmd.EnableShaderKeyword("UNITY_SINGLE_PASS_STEREO");
            }}
#endif
#if UNITY_ANDROID && ENABLE_VR_MODULE && ENABLE_VR
            // {MARKER}: restore the camera's stereo mode for whatever renders after the stack
            if (context.stereoActive && context.numberOfEyes > 1 && context.stereoRenderingMode == PostProcessRenderContext.StereoRenderingMode.SinglePassInstanced)
                cmd.SetSinglePassStereo(XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.SinglePassMultiview
                    ? SinglePassStereoMode.Multiview
                    : SinglePassStereoMode.Instancing);
#endif
"""),
]


def main() -> int:
    packages = Path(sys.argv[1]) / "Packages"
    for relative, anchor, replacement in PATCHES:
        path = packages / relative
        text = path.read_text()
        if replacement in text:
            print(f"already patched: {relative}")
            continue
        if text.count(anchor) != 1:
            print(f"anchor not found exactly once in {relative}", file=sys.stderr)
            return 1
        path.chmod(0o644)
        path.write_text(text.replace(anchor, replacement), newline="\n")
        print(f"patched: {relative}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
