using System.Collections;
using RepoXR.UI;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RepoXR.Standalone
{
    /// <summary>Fill missing Russian entries in the supported game's Menu table without replacing its translations.</summary>
    public sealed class RussianMenuTranslations : MonoBehaviour
    {
        private static readonly string[] Keys =
        {
            "SETTINGS.GAMEPAD", "SETTINGS.CONTROLS.CHAT_CONFIRM",
            "SETTINGS.LANGUAGE.CUSTOM_TRANSLATIONS", "SETTINGS.GAMEPAD.ROTATION_SENSITIVITY"
        };

        private void OnEnable()
        {
            LocalizationSettings.SelectedLocaleChanged += LocaleChanged;
            StartCoroutine(Repair());
        }

        private void OnDisable()
        {
            LocalizationSettings.SelectedLocaleChanged -= LocaleChanged;
            StopAllCoroutines();
        }

        private void LocaleChanged(Locale locale) => StartCoroutine(Repair());

        private IEnumerator Repair()
        {
            yield return LocalizationSettings.InitializationOperation;
            var locale = LocalizationSettings.SelectedLocale;
            if (!locale || !locale.Identifier.Code.StartsWith("ru", System.StringComparison.OrdinalIgnoreCase)) yield break;
            var operation = LocalizationSettings.StringDatabase.GetTableAsync("Menu", locale);
            yield return operation;
            if (operation.Status != AsyncOperationStatus.Succeeded || !operation.Result)
            {
                Debug.LogWarning("[QuestLocale] could not load the Russian Menu table");
                yield break;
            }
            if (LocalizationSettings.SelectedLocale != locale) yield break;
            foreach (var key in Keys)
                if (string.IsNullOrEmpty(operation.Result.GetEntry(key)?.LocalizedValue))
                    operation.Result.AddEntry(key, QuestText.Get(key));
            // Refresh labels that already displayed an English fallback while the table was loading.
            foreach (var text in Object.FindObjectsByType<LocalizeStringEvent>(FindObjectsSortMode.None))
                text.RefreshString();
        }
    }
}
