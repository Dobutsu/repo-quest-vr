using System;

namespace RepoQuest.Localization
{
    public static class DialogLabels
    {
        // Recognize both translations so changing the game language can restore the English labels too.
        public static string BinaryChoiceKey(string label)
        {
            label = label?.Trim();
            if (string.Equals(label, "Yes", StringComparison.OrdinalIgnoreCase) || string.Equals(label, "Да", StringComparison.OrdinalIgnoreCase)) return "ui.yes";
            if (string.Equals(label, "No", StringComparison.OrdinalIgnoreCase) || string.Equals(label, "Нет", StringComparison.OrdinalIgnoreCase)) return "ui.no";
            return null;
        }
    }
}
