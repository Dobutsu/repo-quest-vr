using RepoXR.ThirdParty.MRTK;
using RepoXR.UI.Menu;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RepoXR.Standalone
{
    /// <summary>One of the game's popups, kept so that its text can change while it is open, and closed from code</summary>
    internal sealed class MenuPopup
    {
        private readonly int dismissAnswer;
        private MenuPage page;
        private TextMeshProUGUI body;
        private MenuButton button; // a one-button popup's
        private int answer;
        private bool closedHere;

        private MenuPopup(int dismissAnswer)
        {
            this.dismissAnswer = dismissAnswer;
        }

        /// <summary>
        /// 0 while open; otherwise the option chosen (1 or 2). A popup with one button answers 1; a two-option popup that
        /// disappears without an answer (the menu went away) counts as option 2, the way back.
        /// </summary>
        public int Answer
        {
            get
            {
                if (answer == 0 && !closedHere && (!page || page.currentPageState == MenuPage.PageState.Closing))
                    answer = dismissAnswer;
                return answer;
            }
        }

        public bool IsOpen => page && page.currentPageState != MenuPage.PageState.Closing;

        /// <summary>A message with one button, which closes it</summary>
        public static MenuPopup Message(string header, Color color, string text, string button)
        {
            var popup = new MenuPopup(1);
            if (MenuManager.instance && MenuManager.instance.currentMenuPage)
            {
                MenuPagePopUp.instance = null;
                MenuManager.instance.PagePopUp(header, color, text, button, true);
                popup.Attach(MenuPagePopUp.instance ? MenuPagePopUp.instance.GetComponent<MenuPage>() : null);
            }
            return popup;
        }

        /// <summary>
        /// A question with two options; <paramref name="host"/> carries the events the popup copies.
        /// <paramref name="dismissAnswer"/>: the answer when the popup goes away unanswered (option 2 unless that would
        /// decide something the player did not choose)
        /// </summary>
        public static MenuPopup Ask(MenuButtonPopUp host, string header, Color color, string text, string option1, string option2,
                                    int dismissAnswer = 2)
        {
            var popup = new MenuPopup(dismissAnswer);
            if (!MenuManager.instance || !MenuManager.instance.currentMenuPage)
                return popup;

            host.option1Event = new UnityEvent();
            host.option1Event.AddListener(() => popup.answer = 1);
            host.option2Event = new UnityEvent();
            host.option2Event.AddListener(() => popup.answer = 2);
            // R.E.P.O. 0.5.0 opens popups on top of the current page without making them current (PageOpen(..., true)):
            // the popup is the instance its Awake just set. Attached to the current page instead (the main menu), the
            // popup counted as gone at once and the login ended before LOG IN was pressed (1.0.7-qc12/qc14)
            MenuPageTwoOptions.instance = null;
            MenuManager.instance.PagePopUpTwoOptions(host, header, null, color, text, null, option1, null, option2, null, true);
            popup.Attach(MenuPageTwoOptions.instance ? MenuPageTwoOptions.instance.GetComponent<MenuPage>() : null);
            if (!popup.page)
                Debug.LogWarning("[RepoXR.Standalone] the game's two-option popup did not open");
            return popup;
        }

        public void SetText(string text)
        {
            if (body && body.text != text)
                body.text = text;
        }

        /// <summary>A one-button popup's label (MenuPagePopUp.Update copies it to the button)</summary>
        public void SetButton(string label)
        {
            if (button)
                button.buttonTextString = label;
        }

        /// <summary>Closes it as its buttons do, without counting as an answer</summary>
        public void Close()
        {
            if (!IsOpen)
                return;

            Debug.Log($"[RepoXR.Standalone] text page closed (answer {Answer})");

            closedHere = true;
            if (MenuManager.instance)
                MenuManager.instance.PageReactivatePageUnderThisPage(page);
            page.PageStateSet(MenuPage.PageState.Closing);
        }

        private void Attach(MenuPage opened)
        {
            if (!opened || (!opened.GetComponent<MenuPagePopUp>() && !opened.GetComponent<MenuPageTwoOptions>()))
                return;

            page = opened;
            var single = opened.GetComponent<MenuPagePopUp>();
            if (single)
            {
                body = single.bodyTextMesh;
                button = single.okButton;
            }
            else
                body = opened.GetComponent<MenuPageTwoOptions>().bodyTextMesh;
        }
    }

    /// <summary>
    /// A text entry page, made from the game's text prompt page (R.E.P.O. 0.5.0: MenuPagePopUpTextInput, which replaced
    /// the "rename save file" page): a header, one text field with RepoXR's VR keyboard under the menu, and two buttons.
    /// The page's own component (which would answer the game's prompt) and its localized texts are removed.
    /// <see cref="Show"/> can reuse the page for the next field, so the keyboard stays.
    /// </summary>
    internal sealed class MenuTextPage : MonoBehaviour
    {
        private const int MaskedLength = 24;

        private MenuPage page;
        private MenuTextInput input;
        private TextMeshProUGUI header;
        private MenuButton confirmButton;
        private MenuButton backButton;
        private string headerText = "";
        private Color headerColor = Color.white;
        private bool masked;
        private NonNativeKeyboard keyboard;
        private bool closing;
        private MenuPage.PageState? reportedState;

        /// <summary>0 while typing; 1 confirmed (never with an empty field), 2 back</summary>
        public int Answer { get; private set; }

        public string Text => input.textCurrent;

        public bool IsOpen => page && !closing && page.currentPageState != MenuPage.PageState.Closing;

        /// <summary>On top of the current page, which comes back when this one closes; null if that failed</summary>
        public static MenuTextPage Open()
        {
            if (!MenuManager.instance || !MenuManager.instance.currentMenuPage)
                return null;

            // Opened the way the game opens it (MenuManager.PagePopUpTextInput: a popup added on top of the current
            // page). Opened as a page of its own (PageOpenOnTop) it never showed on the headset: the login popup closed
            // and the main menu came back (1.0.7-qc12)
            MenuPagePopUpTextInput.instance = null;
            MenuManager.instance.PagePopUpTextInput("", null, Color.white, "", null, confirmText: "OK", defaultText: "",
                localizedConfirm: null, upperOnly: false, maxLength: 64, allowEmpty: false, confirmEvent: null,
                cancelEvent: null);
            var prompt = MenuPagePopUpTextInput.instance;
            var page = prompt ? prompt.GetComponent<MenuPage>() : null;
            if (!page)
            {
                Debug.LogWarning("[RepoXR.Standalone] the game's text prompt page did not open; no text page for the Steam login");
                return null;
            }

            // Now, before any Start of the new page runs: the prompt's logic, and localized texts that would replace ours
            var textPage = page.gameObject.AddComponent<MenuTextPage>();
            textPage.page = page;
            textPage.input = prompt ? prompt.menuTextInput : page.GetComponentInChildren<MenuTextInput>(true);
            textPage.header = page.menuHeader;
            textPage.confirmButton = prompt ? prompt.confirmButton : null;
            textPage.backButton = prompt ? prompt.backButton : null;
            if (prompt && prompt.bodyTextMesh)
                prompt.bodyTextMesh.text = "";
            if (prompt)
                DestroyImmediate(prompt);
            foreach (var localized in page.GetComponentsInChildren<LocalizationChangedEvent>(true))
                DestroyImmediate(localized);
            if (!textPage.input || !textPage.header || !textPage.confirmButton || !textPage.backButton)
            {
                Debug.LogWarning("[RepoXR.Standalone] the game's text prompt page changed; no text page for the Steam login");
                textPage.Close();
                return null;
            }

            textPage.Wire(textPage.confirmButton, 1);
            textPage.Wire(textPage.backButton, 2);

            // Longer headers than the page was made for shrink instead of running out of the panel
            textPage.header.enableWordWrapping = false;
            textPage.header.fontSizeMax = textPage.header.fontSize;
            textPage.header.fontSizeMin = 16f;
            textPage.header.enableAutoSizing = true;
            var rect = (RectTransform)page.transform;
            Debug.Log($"[RepoXR.Standalone] text page open: state {page.currentPageState}, at {rect.position} (local {rect.localPosition}), " +
                      $"under {(page.pageUnderThisPage ? page.pageUnderThisPage.name : "-")}");
            return textPage;
        }

        /// <summary>Sets the page up for a field; <paramref name="masked"/> shows the text as asterisks</summary>
        public void Show(string title, string confirm, string back, string text, bool masked = false, bool upperOnly = false,
            int maxLength = 64)
        {
            headerText = title;
            headerColor = Color.white;
            this.masked = masked;
            input.upperOnly = upperOnly;
            input.maxLength = maxLength;
            input.textCurrent = text ?? "";
            SetLabel(confirmButton, confirm);
            SetLabel(backButton, back);
            Answer = 0;
            ApplyHeader();
        }

        /// <summary>The field was rejected (a wrong code): says so in the header and empties the field</summary>
        public void Warn(string title)
        {
            headerText = title;
            headerColor = Color.red;
            input.textCurrent = "";
            Answer = 0;
            ApplyHeader();
        }

        /// <summary>A status in the header while the field is being checked</summary>
        public void SetTitle(string title)
        {
            headerText = title;
            headerColor = Color.white;
            ApplyHeader();
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            Debug.Log($"[RepoXR.Standalone] text page closed (answer {Answer})");

            closing = true;
            if (input)
                input.textCurrent = "";
            CloseKeyboard();
            MenuManager.instance.PageReactivatePageUnderThisPage(page);
            page.PageStateSet(MenuPage.PageState.Closing);
        }

        private void Update()
        {
            if (!IsOpen)
                return;

            if (page.currentPageState != reportedState)
            {
                reportedState = page.currentPageState;
                Debug.Log($"[RepoXR.Standalone] text page {reportedState} at {page.transform.position}");
            }

            // Back, as the game's own text pages answer to it. Not UIClick: in VR that is every trigger press, also
            // the ones on the keyboard's keys, which confirmed the field after its first letter
            if (page.isInteractable && SemiFunc.InputDown(InputKey.Back))
                Choose(2);

            HookKeyboard();
            ApplyHeader();
        }

        // After MenuTextInput.Update has written the plain text: TMP builds the mesh at the end of the frame, so only the
        // asterisks are ever drawn. The cursor follows them (localPosition, as RepoXR's patch of InputTextSet does).
        private void LateUpdate()
        {
            if (!masked || !IsOpen)
                return;

            input.textMain.text = new string('*', Mathf.Min(input.textCurrent.Length, MaskedLength));
            input.textMain.ForceMeshUpdate();
            var width = Mathf.Max(input.textMain.renderedWidth, 0f);
            input.textCursor.transform.localPosition = input.textMain.transform.localPosition + new Vector3(width + 1f, 0f, 0f);
        }

        private void OnDestroy()
        {
            CloseKeyboard();
        }

        private void Choose(int choice)
        {
            if (Answer != 0 || !IsOpen)
                return;

            if (choice == 1 && string.IsNullOrWhiteSpace(input.textCurrent))
            {
                // As the game's text pages refuse an empty name
                confirmButton.OnHovering();
                MenuManager.instance.MenuEffectClick(MenuManager.MenuClickEffectType.Deny, null, 1f, 1f);
                return;
            }

            if (choice == 1)
                MenuManager.instance.MenuEffectClick(MenuManager.MenuClickEffectType.Confirm, null, 1f, 1f);
            Answer = choice;
        }

        private void Wire(MenuButton button, int choice)
        {
            // A new event drops the persistent listeners of the rename page
            var uiButton = button.GetComponent<Button>();
            uiButton.onClick = new Button.ButtonClickedEvent();
            uiButton.onClick.AddListener(() => Choose(choice));
        }

        private static void SetLabel(MenuButton button, string label)
        {
            button.buttonTextString = label;
            button.buttonText.text = label;
        }

        private void ApplyHeader()
        {
            if (header.text != headerText)
                header.text = headerText;
            header.color = headerColor;
        }

        /// <summary>RepoXR's keyboard comes with the text field (MenuTextInput.Start); its Enter confirms here too</summary>
        private void HookKeyboard()
        {
            if (keyboard)
                return;

            var host = InputKeyboard.instance;
            if (!host || host.menuInput != input)
                return;

            keyboard = host.GetComponentInChildren<NonNativeKeyboard>(true);
            if (keyboard)
                keyboard.OnKeyboardFunctionKeyPressed += OnFunctionKey;
        }

        private void OnFunctionKey(KeyboardKeyFunc key)
        {
            if (key.ButtonFunction == KeyboardKeyFunc.Function.Enter && MenuManager.instance.currentMenuPage == page)
                Choose(1);
        }

        private void CloseKeyboard()
        {
            if (keyboard)
                keyboard.OnKeyboardFunctionKeyPressed -= OnFunctionKey;
            keyboard = null;

            var host = InputKeyboard.instance;
            if (host && host.menuInput == input)
                host.Close();
        }
    }
}
