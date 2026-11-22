using System;

namespace RepoQuest.UI
{
    public readonly struct ModalMetrics
    {
        public const float Width = 640, Padding = 18, HeaderHeight = 32, HeaderGap = 10;
        public const float ButtonWidth = 290, ButtonHeight = 34, RowGap = 8, ButtonsGap = 14;
        public const float InputHeight = 34, InputGap = 12, MaxHeight = 390;
        public readonly float Height, BodyHeight, BodyTop, ButtonsTop, InputCenter;
        public readonly int Rows;

        public ModalMetrics(float preferredBodyHeight, int buttonCount, bool hasInput)
        {
            Rows = Math.Max(1, (buttonCount + 1) / 2);
            var fixedHeight = Padding * 2 + HeaderHeight + HeaderGap + ButtonsGap +
                Rows * ButtonHeight + (Rows - 1) * RowGap + (hasInput ? InputHeight + InputGap : 0);
            BodyHeight = Math.Min(Math.Max(36, preferredBodyHeight), MaxHeight - fixedHeight);
            Height = fixedHeight + BodyHeight;
            BodyTop = Height / 2 - Padding - HeaderHeight - HeaderGap;
            InputCenter = BodyTop - BodyHeight - InputGap - InputHeight / 2;
            ButtonsTop = BodyTop - BodyHeight - (hasInput ? InputGap + InputHeight : 0) - ButtonsGap;
        }

        public float ButtonCenterY(int index) => ButtonsTop - ButtonHeight / 2 - (index / 2) * (ButtonHeight + RowGap);
        public float ButtonCenterX(int index, int count) => index == count - 1 && count % 2 == 1 ? 0 : (index % 2 == 0 ? -1 : 1) * 153;
    }
}
