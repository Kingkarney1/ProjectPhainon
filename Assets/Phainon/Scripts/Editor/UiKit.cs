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
            // A flat card with a hairline border and bracketed corners, as on avionics pages.
            Image(section, theme.panelFill);
            Color line = theme.panelOutline;
            Image("Border Top", section, 0f, 0f, width, 1f, line);
            Image("Border Bottom", section, 0f, height - 1f, width, 1f, line);
            Image("Border Left", section, 0f, 0f, 1f, height, line);
            Image("Border Right", section, width - 1f, 0f, 1f, height, line);
            Color bracket = Color.Lerp(theme.panelOutline, theme.accentBlue, 0.6f);
            const float arm = 9f, weight = 2f;
            for (int cx = 0; cx < 2; cx++)
            {
                for (int cy = 0; cy < 2; cy++)
                {
                    Image("Bracket", section, cx == 0 ? 0f : width - arm, cy == 0 ? 0f : height - weight, arm, weight, bracket);
                    Image("Bracket", section, cx == 0 ? 0f : width - weight, cy == 0 ? 0f : height - arm, weight, arm, bracket);
                }
            }
            if (!string.IsNullOrEmpty(title))
            {
                Image("Title Mark", section, 12f, 9f, 3f, 14f, theme.accentBlue);
                TextMeshProUGUI heading = Text("Title", section, 21f, 6f, width - 33f, 20f, title, 13.5f, theme.accentBlue, TextAlignmentOptions.Left, FontStyles.Bold);
                heading.characterSpacing = 6f;
                Image("Title Rule", section, 12f, 28f, width - 24f, 1f, line);
            }
            return section;
        }

        /// <summary>Caption on the left, value right-aligned, unit at the far right.</summary>
        public ValueReadout Readout(Transform parent, float x, float y, float width, float height, string label, string unit, float valueSize = 26f)
        {
            RectTransform root = Rect("Readout " + label, parent, x, y, width, height);
            var readout = root.gameObject.AddComponent<ValueReadout>();
            const float unitWidth = 46f;
            readout.label = Text("Label", root, 0f, 0f, width * 0.5f, height, label, 12.5f, theme.textDim, TextAlignmentOptions.Left);
            readout.label.characterSpacing = 4f;
            readout.value = Text("Value", root, width * 0.3f, 0f, width * 0.7f - unitWidth - 6f, height, "--", valueSize, theme.textPrimary,
                TextAlignmentOptions.Right, FontStyles.Bold);
            readout.unit = Text("Unit", root, width - unitWidth, 0f, unitWidth, height, unit, 12f, theme.textDim, TextAlignmentOptions.BottomLeft);
            readout.unit.margin = new Vector4(0f, 0f, 0f, height * 0.22f);
            // A faint rule under each figure keeps a column of them in line.
            Image("Rule", root, 0f, height - 1f, width, 1f, Faint(theme.panelOutline, 0.55f));
            return readout;
        }

        /// <summary>A stacked readout: small caption above a large value, for compact rows.</summary>
        public ValueReadout StackedReadout(Transform parent, float x, float y, float width, float height, string label, string unit, float valueSize = 24f)
        {
            RectTransform root = Rect("Readout " + label, parent, x, y, width, height);
            var readout = root.gameObject.AddComponent<ValueReadout>();
            readout.label = Text("Label", root, 0f, 0f, width, 16f, label, 11.5f, theme.textDim, TextAlignmentOptions.Center);
            readout.label.characterSpacing = 4f;
            readout.value = Text("Value", root, 0f, 15f, width, height - 30f, "--", valueSize, theme.textPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
            readout.unit = Text("Unit", root, 0f, height - 16f, width, 16f, unit, 12f, theme.textDim, TextAlignmentOptions.Center);
            return readout;
        }

        public BarGauge Bar(Transform parent, float x, float y, float width, float height, string label)
        {
            RectTransform root = Rect("Gauge " + label, parent, x, y, width, height);
            var gauge = root.gameObject.AddComponent<BarGauge>();
            float barHeight = Mathf.Max(8f, height - 22f);

            gauge.label = Text("Label", root, 0f, 0f, width * 0.6f, 20f, label, 12.5f, theme.textDim, TextAlignmentOptions.Left);
            gauge.label.characterSpacing = 4f;
            gauge.value = Text("Value", root, width * 0.4f, 0f, width * 0.6f, 20f, "--", 15f, theme.textPrimary, TextAlignmentOptions.Right, FontStyles.Bold);

            RectTransform track = Rect("Track", root, 0f, 22f, width, barHeight);
            Image(track, theme.gaugeTrack);
            // End stops, so an empty gauge still shows its range.
            Image("Stop", root, 0f, 20f, 1.5f, barHeight + 4f, theme.panelOutline);
            Image("Stop", root, width - 1.5f, 20f, 1.5f, barHeight + 4f, theme.panelOutline);

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
            // A bright leading edge marks the reading exactly, whatever colour the fill takes.
            var edgeObject = new GameObject("Edge", typeof(RectTransform));
            edgeObject.layer = root.gameObject.layer;
            var edge = (RectTransform)edgeObject.transform;
            edge.SetParent(fill, false);
            edge.anchorMin = new Vector2(1f, 0f);
            edge.anchorMax = Vector2.one;
            edge.pivot = new Vector2(1f, 0.5f);
            edge.offsetMin = new Vector2(-2f, 0f);
            edge.offsetMax = Vector2.zero;
            Image(edge, new Color(1f, 1f, 1f, 0.75f));

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
            row.lamp = Image("Lamp", root, 0f, (height - lamp) * 0.5f, lamp, lamp, theme.textDim, assets.Circle);
            row.label = Text("Label", root, lamp + 8f, 0f, width * 0.58f, height, label, labelSize, theme.textPrimary, TextAlignmentOptions.Left);
            row.status = Text("Status", root, width * 0.42f, 0f, width * 0.58f, height, "--", labelSize, theme.textDim, TextAlignmentOptions.Right, FontStyles.Bold);
            return row;
        }

        private static Color Faint(Color color, float alpha) { return new Color(color.r, color.g, color.b, alpha); }

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
