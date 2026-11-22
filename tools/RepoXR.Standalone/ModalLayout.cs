using System.Linq;
using RepoQuest.UI;
using RepoXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RepoXR.Standalone
{
    /// <summary>Fit the game's modal panel around all controls, retaining its menu and VR input components.</summary>
    internal sealed class ModalLayout : MonoBehaviour
    {
        private MenuPage page;
        private TextMeshProUGUI body;
        private MenuTextInput input;
        private RectTransform viewport;
        private ScrollRect scroll;
        private Scrollbar scrollbar;
        private string previous;

        internal static void Attach(MenuPage page, TextMeshProUGUI body, MenuTextInput input = null)
        {
            var layout = page.GetComponent<ModalLayout>() ?? page.gameObject.AddComponent<ModalLayout>();
            layout.page = page;
            layout.body = body;
            layout.input = input;
            layout.previous = null;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.localScale = Vector3.one;
            rect.anchoredPosition = position;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
        }

        private void LateUpdate()
        {
            if (!page || !body || !page.menuHeader) return;
            var buttons = GetComponentsInChildren<MenuButton>(false);
            var signature = QuestText.Locale + "|" + page.menuHeader.text + "|" + body.text + "|" +
                string.Join("|", buttons.Select(b => b.name + b.buttonTextString));
            if (signature == previous) return;
            previous = signature;
            var root = (RectTransform)page.transform;
            var panel = page.transform.Find("Panel") as RectTransform;
            if (!panel) { Debug.LogWarning("[Network UI] modal Panel missing: " + page.name); return; }
            foreach (var group in GetComponentsInChildren<HorizontalLayoutGroup>(true)) group.enabled = false;
            foreach (var fitter in GetComponentsInChildren<ContentSizeFitter>(true)) fitter.enabled = false;

            body.fontSize = 20;
            body.fontSizeMin = 16;
            body.fontSizeMax = 20;
            body.enableAutoSizing = false;
            body.textWrappingMode = TextWrappingModes.Normal;
            body.alignment = TextAlignmentOptions.TopLeft;
            var contentWidth = ModalMetrics.Width - ModalMetrics.Padding * 2 - 20;
            var preferred = body.GetPreferredValues(body.text, contentWidth, Mathf.Infinity).y + 8;
            var metrics = new ModalMetrics(preferred, buttons.Length, input);
            Place(panel, Vector2.zero, new Vector2(ModalMetrics.Width, metrics.Height));
            page.menuHeader.rectTransform.SetParent(root, false);
            Place(page.menuHeader.rectTransform, new Vector2(0, metrics.Height / 2 - ModalMetrics.Padding - ModalMetrics.HeaderHeight / 2),
                new Vector2(ModalMetrics.Width - ModalMetrics.Padding * 2, ModalMetrics.HeaderHeight));
            page.menuHeader.fontSizeMax = 26;
            page.menuHeader.fontSizeMin = 18;
            page.menuHeader.enableAutoSizing = true;
            if (!viewport) CreateViewport(root);
            Place(viewport, new Vector2(0, metrics.BodyTop - metrics.BodyHeight / 2), new Vector2(ModalMetrics.Width - ModalMetrics.Padding * 2, metrics.BodyHeight));
            var content = body.rectTransform;
            content.SetParent(viewport, false);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f, 1);
            content.localScale = Vector3.one;
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(contentWidth, Mathf.Max(preferred, metrics.BodyHeight));
            body.overflowMode = TextOverflowModes.Overflow;
            scrollbar.gameObject.SetActive(preferred > metrics.BodyHeight);
            scroll.verticalNormalizedPosition = 1;
            if (input)
            {
                input.transform.SetParent(root, false);
                Place((RectTransform)input.transform, new Vector2(0, metrics.InputCenter),
                    new Vector2(ModalMetrics.Width - ModalMetrics.Padding * 2, ModalMetrics.InputHeight));
            }
            for (var i = 0; i < buttons.Length; i++)
            {
                buttons[i].transform.SetParent(root, false);
                Place((RectTransform)buttons[i].transform, new Vector2(metrics.ButtonCenterX(i, buttons.Length), metrics.ButtonCenterY(i)),
                    new Vector2(ModalMetrics.ButtonWidth, ModalMetrics.ButtonHeight));
                MenuActions.Center(buttons[i], new Vector2(ModalMetrics.ButtonWidth, ModalMetrics.ButtonHeight));
            }
            page.WireVerticalNavigation();
        }

        private void CreateViewport(RectTransform root)
        {
            var host = new GameObject("Modal text viewport", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect));
            viewport = (RectTransform)host.transform;
            viewport.SetParent(root, false);
            scroll = host.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = body.rectTransform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24;
            var track = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            var trackRect = (RectTransform)track.transform;
            trackRect.SetParent(viewport, false);
            trackRect.anchorMin = new Vector2(1, 0);
            trackRect.anchorMax = Vector2.one;
            trackRect.pivot = new Vector2(1, .5f);
            trackRect.sizeDelta = new Vector2(14, 0);
            trackRect.anchoredPosition = Vector2.zero;
            track.GetComponent<Image>().color = new Color(.15f, .15f, .15f, 1);
            scrollbar = track.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            var handleRect = (RectTransform)handle.transform;
            handleRect.SetParent(trackRect, false);
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = handleRect.offsetMax = Vector2.zero;
            var graphic = handle.GetComponent<Image>();
            graphic.color = new Color(.8f, .8f, .8f, 1);
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = graphic;
            scroll.verticalScrollbar = scrollbar;
            viewport.SetSiblingIndex(Mathf.Min(2, root.childCount - 1));
        }
    }
}
