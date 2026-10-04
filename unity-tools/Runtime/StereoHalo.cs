using UnityEngine;
using UnityEngine.Rendering;

namespace RepoQuest
{
    /// <summary>
    /// Stands in for a Halo component: Unity's built-in halo is drawn for the first eye only with single-pass instanced
    /// (multiview) stereo, so lamps and headlights glowed in the left eye and not in the right. scripts/stereo_halos.py
    /// adds one next to every Halo in the game's prefabs with its colour and size, and sets the Halo's own size to 0 so
    /// it draws nothing. The game still switches the Halo on and off (PropLight when lights go out); this follows it.
    /// </summary>
    [DisallowMultipleComponent]
    public class StereoHalo : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static Mesh quad;
        private static Material material;

        public Color color = Color.white;
        public float size = 0.5f;

        private Behaviour halo;
        private MeshRenderer glow;

        private void Awake()
        {
            halo = GetComponent("Halo") as Behaviour;

            if (material == null)
            {
                var shader = Shader.Find("RepoQuest/Halo");
                if (shader == null)
                {
                    Debug.LogWarning("[StereoHalo] shader RepoQuest/Halo missing");
                    enabled = false;
                    return;
                }
                material = new Material(shader) { enableInstancing = true };
                quad = CreateQuad();
            }

            // A child scaled by the halo's size: the vertex shader turns it into a camera-facing quad of that radius,
            // and the renderer's bounds (a cube of that size) keep culling right
            var child = new GameObject("Stereo Halo") { hideFlags = HideFlags.DontSave };
            child.layer = gameObject.layer;
            child.transform.SetParent(transform, false);
            child.transform.localScale = Vector3.one * size / Mathf.Max(1e-4f, MaxScale(transform.lossyScale));
            child.AddComponent<MeshFilter>().sharedMesh = quad;
            glow = child.AddComponent<MeshRenderer>();
            glow.sharedMaterial = material;
            glow.shadowCastingMode = ShadowCastingMode.Off;
            glow.receiveShadows = false;
            glow.lightProbeUsage = LightProbeUsage.Off;
            glow.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var block = new MaterialPropertyBlock();
            block.SetColor(ColorId, color);
            glow.SetPropertyBlock(block);
        }

        private void LateUpdate()
        {
            var on = halo == null || halo.enabled;
            if (glow != null && glow.enabled != on)
                glow.enabled = on;
        }

        private void OnDisable()
        {
            if (glow != null)
                glow.enabled = false;
        }

        private static float MaxScale(Vector3 s) => Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "Stereo Halo" };
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            // The quad faces the camera in any orientation: bound it as a cube
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }
    }
}
