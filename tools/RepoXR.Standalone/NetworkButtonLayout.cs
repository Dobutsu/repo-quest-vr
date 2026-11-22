using UnityEngine;

namespace RepoXR.Standalone
{
    /// <summary>Align Network play with Settings using the native row height and text padding.</summary>
    internal sealed class NetworkButtonLayout : MonoBehaviour
    {
        internal MenuButton Source;
        private MenuButton button;
        private readonly Vector3[] corners = new Vector3[4];

        private void Awake() => button = GetComponent<MenuButton>();

        private void LateUpdate()
        {
            if (!Source || !button) return;
            var rect = (RectTransform)transform;
            var reference = (RectTransform)Source.transform;
            reference.GetWorldCorners(corners);
            var bottom = transform.parent.InverseTransformPoint(corners[3]);
            var top = transform.parent.InverseTransformPoint(corners[2]);
            var padding = Source.buttonText.margin;
            var width = Mathf.Max(150f, button.buttonText.GetPreferredValues(button.buttonTextString).x + padding.x + padding.z);
            var height = top.y - bottom.y;
            if (Mathf.Abs(rect.rect.width - width) > .1f || Mathf.Abs(rect.rect.height - height) > .1f)
                MenuActions.Center(button, new Vector2(width, height));
            button.buttonText.margin = padding;
            rect.localPosition = new Vector3(top.x + 12f + width * rect.pivot.x,
                bottom.y + height * rect.pivot.y, bottom.z);
            button.originalButtonPosition = rect.localPosition;

            // The existing Steam build label used to occupy the same right-hand row.
            var version = transform.parent.Find("RepoQuest Version") as RectTransform;
            if (version)
                version.localPosition = new Vector3(top.x + 12f, top.y + 12f + version.rect.height * version.pivot.y, bottom.z);
        }
    }
}
