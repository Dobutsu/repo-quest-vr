using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RepoXR.Standalone
{
    // Clone the game's selectable buttons, retaining the VR ray and menu navigation support.
    internal static class MenuActions
    {
        public static MenuButton Add(MenuButton source, string label, Vector2 offset, UnityAction action)
        {
            var copy = Object.Instantiate(source.gameObject, source.transform.parent);
            copy.name = "Cloud action - " + label;
            foreach (var localized in copy.GetComponentsInChildren<LocalizationChangedEvent>(true))
                Object.DestroyImmediate(localized);
            if (copy.GetComponent<MenuButtonPopUp>() is MenuButtonPopUp popup) Object.DestroyImmediate(popup);
            var button = copy.GetComponent<MenuButton>();
            button.menuButtonPopUp = null;
            button.hasHold = false;
            button.holdLogic = false;
            button.resizeButton = false;
            var rect = (RectTransform)copy.transform;
            // Auxiliary actions use explicit coordinates in the source's button group. The game's horizontal
            // layout must not move them into the confirm/back row.
            var layout = copy.GetComponent<LayoutElement>() ?? copy.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            rect.anchoredPosition = offset;
            Center(button, new Vector2(150f, 28f));
            button.buttonTextString = label;
            button.buttonText.text = label;
            RepoXR.UI.QuestText.Fit(button.buttonText);
            var ui = copy.GetComponent<Button>();
            ui.onClick = new Button.ButtonClickedEvent();
            ui.onClick.AddListener(action);
            var navigation = new Navigation { mode = Navigation.Mode.Automatic };
            ui.navigation = navigation;
            if (button.menuSelectableElement) button.menuSelectableElement.navigation = navigation;
            return button;
        }

        public static void Center(MenuButton button, Vector2 size)
        {
            // ResizeToText restores Awake's alignment/margins and hover restores the text's original position.
            // A fixed hit rectangle keeps both localization and hover from undoing the centered layout.
            button.resizeButton = false;
            var rect = (RectTransform)button.transform;
            rect.sizeDelta = size;
            button.originalButtonPosition = rect.localPosition;
            if (button.rectTransformSelection && button.rectTransformSelection != rect &&
                button.rectTransformSelection.parent == rect)
            {
                var selection = button.rectTransformSelection;
                selection.anchorMin = Vector2.zero;
                selection.anchorMax = Vector2.one;
                selection.offsetMin = selection.offsetMax = Vector2.zero;
            }
            var text = button.buttonText;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.margin = new Vector4(6f, 0f, 6f, 0f);
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 14f;
            text.fontSizeMax = 20f;
            text.enableWordWrapping = false;
            button.buttonTextSelectedOriginalPos = text.transform.localPosition;
            button.buttonTextHoverPos = text.transform.localPosition + Vector3.up;
        }
    }
}
