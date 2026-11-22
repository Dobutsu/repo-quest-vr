using HarmonyLib;
using RepoQuest.Localization;
using RepoXR.UI;

namespace RepoXR.Standalone
{
    [HarmonyPatch(typeof(MenuPageTwoOptions), "Update")]
    public static class DialogLocalizationPatch
    {
        public static void Postfix(MenuPageTwoOptions __instance)
        {
            Translate(__instance.option1Button);
            Translate(__instance.option2Button);
        }

        private static void Translate(MenuButton button)
        {
            if (!button || !button.buttonText) return;
            var key = DialogLabels.BinaryChoiceKey(button.buttonTextString);
            if (key == null) return;
            var label = QuestText.Get(key);
            // The game restores buttonTextString every frame; translating only TMP text is overwritten.
            button.buttonTextString = label;
            button.buttonText.text = label;
            QuestText.Fit(button.buttonText);
        }
    }
}
