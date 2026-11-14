using Phainon.Displays;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Phainon.EditorTools
{
    /// <summary>
    /// Helpers for laying out UI in code. Positions are in pixels from the parent's top-left corner,
    /// which keeps display layouts readable as simple x, y, width, height rows.
    /// </summary>
    public class UiKit
    {
        private readonly AssetFactory assets;
        private readonly CockpitTheme theme;

        public UiKit(AssetFactory assets)
        {
            this.assets = assets;
            theme = assets.Theme;
        }

        public CockpitTheme Theme { get { return theme; } }

        public RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        /// <summary>A rect that fills its parent, optionally inset by a margin.</summary>
        public RectTransform Fill(string name, Transform parent, float margin = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect, margin);
            return rect;
        }

        public static void Stretch(RectTransform rect, float margin = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
        }

        /// <summary>A rect positioned by its centre, for elements that move or rotate.</summary>
        public RectTransform Centred(string name, Transform parent, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public Image Image(RectTransform rect, Color color, Sprite sprite = null, bool sliced = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.raycastTarget = false;
            if (sliced && sprite != null) image.type = UnityEngine.UI.Image.Type.Sliced;
            return image;
        }

        public Image Image(string name, Transform parent, float x, float y, float width, float height, Color color, Sprite sprite = null)
        {
            return Image(Rect(name, parent, x, y, width, height), color, sprite);
        }

        public TextMeshProUGUI Text(string name, Transform parent, float x, float y, float width, float height, string content,
            float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal, bool wrap = false)
        {
            return Text(Rect(name, parent, x, y, width, height), content, size, color, alignment, style, wrap);
        }

        public TextMeshProUGUI Text(RectTransform rect, string content, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal, bool wrap = false)
        {
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.overflowMode = wrap ? TextOverflowModes.Truncate : TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.richText = true;
            return text;
        }

        // ---------------------------------------------------------------- display building blocks

        /// <summary>A titled, rounded section. Returns the section rect; place children in its pixel space.</summary>
        public RectTransform Section(Transform parent, float x, float y, float width, float height, string title)
        {
            RectTransform section = Rect("Section " + title, parent, x, y, width, height);
            Image(section, theme.panelFill, assets.RoundedPanel, true);
            if (!string.IsNullOrEmpty(title))
            {
                Text("Title", section, 12f, 6f, width - 24f, 20f, title, 15f, theme.accentBlue, TextAlignmentOptions.Left, FontStyles.Bold);
                Image("Title Rule", section, 12f, 28f, width - 24f, 1.5f, theme.panelOutline);
            }
            return section;
        }

        /// <summary>Caption on the left, value right-aligned, unit at the far right.</summary>
        public ValueReadout Readout(Transform parent, float x, float y, float width, float height, string label, string unit, float valueSize = 26f)
        {
            RectTransform root = Rect("Readout " + label, parent, x, y, width, height);
            var readout = root.gameObject.AddComponent<ValueReadout>();
            const float unitWidth = 46f;
            readout.label = Text("Label", root, 0f, 0f, width * 0.5f, height, label, 14f, theme.textDim, TextAlignmentOptions.Left);
            readout.value = Text("Value", root, width * 0.3f, 0f, width * 0.7f - unitWidth - 6f, height, "--", valueSize, theme.textPrimary,
                TextAlignmentOptions.Right, FontStyles.Bold);
            readout.unit = Text("Unit", root, width - unitWidth, 0f, unitWidth, height, unit, 13f, theme.textDim, TextAlignmentOptions.BottomLeft);
            readout.unit.margin = new Vector4(0f, 0f, 0f, height * 0.22f);
            return readout;
        }

        /// <summary>A stacked readout: small caption above a large value, for compact rows.</summary>
        public ValueReadout StackedReadout(Transform parent, float x, float y, float width, float height, string label, string unit, float valueSize = 24f)
        {
            RectTransform root = Rect("Readout " + label, parent, x, y, width, height);
            var readout = root.gameObject.AddComponent<ValueReadout>();
            readout.label = Text("Label", root, 0f, 0f, width, 16f, label, 12.5f, theme.textDim, TextAlignmentOptions.Center);
            readout.value = Text("Value", root, 0f, 15f, width, height - 30f, "--", valueSize, theme.textPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
            readout.unit = Text("Unit", root, 0f, height - 16f, width, 16f, unit, 12f, theme.textDim, TextAlignmentOptions.Center);
            return readout;
        }

        public BarGauge Bar(Transform parent, float x, float y, float width, float height, string label)
        {
            RectTransform root = Rect("Gauge " + label, parent, x, y, width, height);
            var gauge = root.gameObject.AddComponent<BarGauge>();
            float barHeight = Mathf.Max(8f, height - 22f);

            gauge.label = Text("Label", root, 0f, 0f, width * 0.6f, 20f, label, 14f, theme.textDim, TextAlignmentOptions.Left);
            gauge.value = Text("Value", root, width * 0.4f, 0f, width * 0.6f, 20f, "--", 15f, theme.textPrimary, TextAlignmentOptions.Right, FontStyles.Bold);

            RectTransform track = Rect("Track", root, 0f, 22f, width, barHeight);
            Image(track, theme.gaugeTrack);

            // The fill grows by moving its right anchor, so no sprite or fill mode is required.
            var fillObject = new GameObject("Fill", typeof(RectTransform));
            fillObject.layer = root.gameObject.layer;
            var fill = (RectTransform)fillObject.transform;
            fill.SetParent(track, false);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0.5f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            gauge.fill = fill;
            gauge.fillImage = Image(fill, theme.nominalGreen);

            // Quarter marks over the track.
            for (int i = 1; i < 4; i++)
                Image("Tick", track, width * i * 0.25f - 0.75f, 0f, 1.5f, barHeight, theme.screenBackground);
            return gauge;
        }

        public StatusRow Status(Transform parent, float x, float y, float width, float height, string label, float labelSize = 15f)
        {
            RectTransform root = Rect("Status " + label, parent, x, y, width, height);
            var row = root.gameObject.AddComponent<StatusRow>();
            float lamp = Mathf.Min(10f, height - 6f);
            row.lamp = Image("Lamp", root, 0f, (height - lamp) * 0.5f, lamp, lamp, theme.textDim);
            row.label = Text("Label", root, lamp + 8f, 0f, width * 0.58f, height, label, labelSize, theme.textPrimary, TextAlignmentOptions.Left);
            row.status = Text("Status", root, width * 0.42f, 0f, width * 0.58f, height, "--", labelSize, theme.textDim, TextAlignmentOptions.Right, FontStyles.Bold);
            return row;
        }

        public LineGraph Line(string name, Transform parent, Color color, float thickness)
        {
            RectTransform rect = Fill(name, parent);
            var line = rect.gameObject.AddComponent<LineGraph>();
            line.color = color;
            line.thickness = thickness;
            line.raycastTarget = false;
            return line;
        }
    }
}
