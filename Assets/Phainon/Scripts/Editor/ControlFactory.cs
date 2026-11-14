using System.Collections.Generic;
using Phainon.Cockpit;
using Phainon.Simulation;
using TMPro;
using UnityEngine;

namespace Phainon.EditorTools
{
    /// <summary>
    /// Builds the physical cockpit controls. Every control is placed in panel coordinates: x across
    /// the panel, y up the panel, with the control standing proud of the surface towards -Z.
    /// Adding a control to the cockpit is one call here plus one case in Spacecraft.HandleControl.
    /// </summary>
    public class ControlFactory
    {
        private readonly AssetFactory assets;
        private readonly SpacecraftController spacecraft;

        private readonly Material bezel;
        private readonly Material cap;
        private readonly Material capRed;
        private readonly Material lens;
        private readonly Material metal;
        private readonly Material knob;
        private readonly Material knobTop;
        private readonly Material marking;
        private readonly Material guardRed;
        private readonly Material recess;
        private readonly Material grip;

        public readonly List<Interactable> Controls = new List<Interactable>();
        public readonly List<TMP_Text> Labels = new List<TMP_Text>();
        public ThrottleLever ThrottleLever { get; private set; }

        public static readonly Color LabelColor = new Color(0.90f, 0.93f, 0.95f);
        public static readonly Color Green = new Color(0.25f, 0.95f, 0.45f);
        public static readonly Color Amber = new Color(1.00f, 0.66f, 0.10f);
        public static readonly Color Red = new Color(1.00f, 0.16f, 0.12f);
        public static readonly Color Blue = new Color(0.25f, 0.62f, 1.00f);
        private static readonly Color LensDark = new Color(0.045f, 0.045f, 0.05f);

        public ControlFactory(AssetFactory assets, SpacecraftController spacecraft)
        {
            this.assets = assets;
            this.spacecraft = spacecraft;

            bezel = assets.Lit("ControlBezel", new Color(0.035f, 0.037f, 0.04f), 0.3f, 0.45f, true);
            cap = assets.Lit("ButtonCap", new Color(0.13f, 0.135f, 0.14f), 0.0f, 0.35f);
            capRed = assets.Lit("ButtonCapRed", new Color(0.42f, 0.05f, 0.04f), 0.0f, 0.4f);
            lens = assets.Lit("LampLens", LensDark, 0.0f, 0.75f, true);
            metal = assets.Lit("SwitchMetal", new Color(0.62f, 0.63f, 0.65f), 0.9f, 0.7f);
            knob = assets.Lit("KnobBody", new Color(0.03f, 0.03f, 0.032f), 0.1f, 0.3f);
            knobTop = assets.Lit("KnobTop", new Color(0.22f, 0.225f, 0.23f), 0.6f, 0.55f);
            marking = assets.Emissive("PanelMarking", new Color(0.8f, 0.82f, 0.84f), new Color(0.16f, 0.17f, 0.18f));
            guardRed = assets.Lit("GuardRed", new Color(0.62f, 0.06f, 0.04f), 0.1f, 0.5f);
            recess = assets.Lit("PanelRecess", new Color(0.012f, 0.012f, 0.013f), 0.0f, 0.2f);
            grip = assets.Lit("LeverGrip", new Color(0.09f, 0.09f, 0.095f), 0.0f, 0.3f, true);
        }

        // ---------------------------------------------------------------- push button

        public InteractiveButton Button(Transform panel, Vector2 at, ControlId id, string label, Color lampColor,
            bool guarded = false, bool redCap = false, float size = 0.044f)
        {
            GameObject root = ControlRoot("Button " + label, panel, at);
            AddCollider(root, new Vector3(0f, guarded ? 0.004f : 0f, -0.014f),
                new Vector3(size + 0.022f, size + (guarded ? 0.034f : 0.022f), guarded ? 0.044f : 0.03f));

            GameObject frame = SceneKit.Box("Bezel", root.transform, new Vector3(0f, 0f, -0.004f),
                new Vector3(size + 0.014f, size + 0.014f, 0.008f), bezel, false, default(Vector3), false);

            GameObject capObject = SceneKit.Box("Cap", root.transform, new Vector3(0f, 0f, -0.012f),
                new Vector3(size, size, 0.012f), redCap ? capRed : cap, false, default(Vector3), false);

            // The lens is parented to the root, not the scaled cap, and follows it through a holder.
            GameObject holder = SceneKit.Empty("Cap Travel", root.transform, Vector3.zero);
            capObject.transform.SetParent(holder.transform, true);
            GameObject lensObject = SceneKit.Box("Lens", holder.transform, new Vector3(0f, size * 0.20f, -0.0185f),
                new Vector3(size * 0.76f, size * 0.26f, 0.002f), lens, false, default(Vector3), false);

            var button = root.AddComponent<InteractiveButton>();
            Configure(button, id, label, frame);
            button.cap = holder.transform;

            if (guarded) button.guard = BuildGuard(root.transform, size);

            AddIndicator(lensObject, (IndicatorId)(int)id, lampColor);
            AddLabel(label, panel, at + new Vector2(0f, size * 0.5f + (guarded ? 0.036f : 0.024f)), 9.5f);
            return button;
        }

        private Transform BuildGuard(Transform root, float size)
        {
            float half = size * 0.5f + 0.010f;
            // Hinged along the top edge; the frame leaves the lamp visible when closed.
            GameObject hinge = SceneKit.Empty("Guard", root, new Vector3(0f, half, -0.010f));
            float depth = 0.020f;
            float rail = 0.005f;
            SceneKit.Box("Hinge Bar", hinge.transform, new Vector3(0f, 0f, -depth * 0.5f), new Vector3(half * 2f + rail, rail * 1.4f, depth), guardRed, false, default(Vector3), false);
            SceneKit.Box("Left Rail", hinge.transform, new Vector3(-half, -half, -depth * 0.5f), new Vector3(rail, half * 2f, depth), guardRed, false, default(Vector3), false);
            SceneKit.Box("Right Rail", hinge.transform, new Vector3(half, -half, -depth * 0.5f), new Vector3(rail, half * 2f, depth), guardRed, false, default(Vector3), false);
            SceneKit.Box("Lip", hinge.transform, new Vector3(0f, -half * 2f, -depth * 0.5f), new Vector3(half * 2f + rail, rail, depth), guardRed, false, default(Vector3), false);
            SceneKit.Box("Cross Bar", hinge.transform, new Vector3(0f, -half * 1.25f, -depth + 0.002f), new Vector3(half * 2f, rail * 1.6f, 0.004f), guardRed, false, default(Vector3), false);
            return hinge.transform;
        }

        // ---------------------------------------------------------------- toggle switch

        public InteractiveSwitch Switch(Transform panel, Vector2 at, ControlId id, string label, bool startOn = false)
        {
            GameObject root = ControlRoot("Switch " + label, panel, at);
            AddCollider(root, new Vector3(0f, 0f, -0.02f), new Vector3(0.05f, 0.07f, 0.05f));

            GameObject plate = SceneKit.Box("Plate", root.transform, new Vector3(0f, 0f, -0.002f),
                new Vector3(0.036f, 0.054f, 0.004f), bezel, false, default(Vector3), false);
            SceneKit.Cylinder("Collar", root.transform, new Vector3(0f, 0f, -0.007f), 0.0095f, 0.008f, metal, Axis.Z);

            GameObject pivot = SceneKit.Empty("Lever", root.transform, new Vector3(0f, 0f, -0.005f));
            SceneKit.Cylinder("Stick", pivot.transform, new Vector3(0f, 0f, -0.016f), 0.0042f, 0.03f, metal, Axis.Z);
            SceneKit.Sphere("Tip", pivot.transform, new Vector3(0f, 0f, -0.032f), 0.0068f, metal);

            GameObject led = SceneKit.Box("Lamp", root.transform, new Vector3(0f, 0.037f, -0.002f),
                new Vector3(0.014f, 0.006f, 0.004f), lens, false, default(Vector3), false);

            var toggle = root.AddComponent<InteractiveSwitch>();
            Configure(toggle, id, label, plate);
            toggle.lever = pivot.transform;
            toggle.isOn = startOn;

            AddIndicator(led, (IndicatorId)(int)id, Green);
            AddLabel(label, panel, at + new Vector2(0f, 0.054f), 8.5f);
            AddLabel("OFF", panel, at + new Vector2(0f, -0.038f), 6f, false);
            return toggle;
        }

        // ---------------------------------------------------------------- rotary dial

        public InteractiveDial Dial(Transform panel, Vector2 at, ControlId id, string label, float startValue,
            string valueFormat, float displayMin, float displayMax, string[] detents = null)
        {
            GameObject root = ControlRoot("Dial " + label, panel, at);
            AddCollider(root, new Vector3(0f, 0f, -0.016f), new Vector3(0.074f, 0.074f, 0.036f));

            GameObject skirt = SceneKit.Cylinder("Skirt", root.transform, new Vector3(0f, 0f, -0.002f), 0.034f, 0.004f, bezel, Axis.Z);

            GameObject knobRoot = SceneKit.Empty("Knob", root.transform, new Vector3(0f, 0f, -0.004f));
            SceneKit.Cylinder("Body", knobRoot.transform, new Vector3(0f, 0f, -0.011f), 0.024f, 0.022f, knob, Axis.Z);
            SceneKit.Cylinder("Top", knobRoot.transform, new Vector3(0f, 0f, -0.0235f), 0.019f, 0.003f, knobTop, Axis.Z);
            SceneKit.Box("Pointer", knobRoot.transform, new Vector3(0f, 0.013f, -0.0255f), new Vector3(0.0035f, 0.018f, 0.002f), marking, false, default(Vector3), false);

            const float minAngle = -135f;
            const float maxAngle = 135f;
            int detentCount = detents != null ? detents.Length : 0;

            // Scale marks around the skirt. Angles are measured clockwise from straight up.
            int marks = detentCount > 1 ? detentCount : 11;
            for (int i = 0; i < marks; i++)
            {
                float angle = Mathf.Lerp(minAngle, maxAngle, i / (float)(marks - 1));
                if (detentCount > 1) angle = DetentAngle(i, detentCount);
                Vector2 direction = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad));
                bool major = detentCount > 1 || i % 5 == 0;
                SceneKit.Box("Mark", root.transform, new Vector3(direction.x * 0.041f, direction.y * 0.041f, -0.0006f),
                    new Vector3(0.0018f, major ? 0.008f : 0.005f, 0.001f), marking, false, new Vector3(0f, 0f, -angle), false);

                if (detentCount > 1)
                    AddLabel(detents[i], panel, at + direction * 0.060f, 6.5f, false);
            }

            var dial = root.AddComponent<InteractiveDial>();
            Configure(dial, id, label, skirt);
            dial.knob = knobRoot.transform;
            dial.detents = detentCount;
            dial.detentLabels = detents;
            dial.value = startValue;
            dial.valueFormat = valueFormat;
            dial.displayMin = displayMin;
            dial.displayMax = displayMax;
            if (detentCount > 1)
            {
                // Narrow the sweep so each detent lines up with its printed position.
                dial.minAngle = DetentAngle(0, detentCount);
                dial.maxAngle = DetentAngle(detentCount - 1, detentCount);
            }
            else
            {
                dial.minAngle = minAngle;
                dial.maxAngle = maxAngle;
            }

            AddLabel(label, panel, at + new Vector2(0f, detentCount > 1 ? 0.079f : 0.060f), 8.5f);
            return dial;
        }

        private static float DetentAngle(int index, int count)
        {
            const float spread = 55f;
            return Mathf.Lerp(-spread, spread, index / (float)(count - 1));
        }

        // ---------------------------------------------------------------- throttle lever

        public ThrottleLever Throttle(Transform panel, Vector2 at, ControlId id, string label)
        {
            GameObject root = ControlRoot("Throttle Lever", panel, at);
            AddCollider(root, new Vector3(0f, 0f, -0.10f), new Vector3(0.12f, 0.30f, 0.22f));

            SceneKit.Box("Quadrant", root.transform, new Vector3(0f, 0f, -0.005f), new Vector3(0.078f, 0.30f, 0.010f), bezel, false, default(Vector3), false);
            SceneKit.Box("Slot", root.transform, new Vector3(0f, 0f, -0.0105f), new Vector3(0.018f, 0.25f, 0.002f), recess, false, default(Vector3), false);

            const float travel = 32f;
            const float armLength = 0.19f;
            // The pivot sits below the panel surface; the arm rises through the slot.
            GameObject pivot = SceneKit.Empty("Pivot", root.transform, new Vector3(0f, 0f, 0.03f));
            SceneKit.Box("Arm", pivot.transform, new Vector3(0f, 0f, -armLength * 0.5f), new Vector3(0.014f, 0.016f, armLength), metal, false, default(Vector3), false);
            GameObject handle = SceneKit.Cylinder("Grip", pivot.transform, new Vector3(0f, 0f, -armLength - 0.012f), 0.019f, 0.10f, grip, Axis.X);
            SceneKit.Cylinder("Grip Band", pivot.transform, new Vector3(0f, 0f, -armLength - 0.012f), 0.0195f, 0.012f, guardRed, Axis.X);

            // Scale: where the grip sits along the panel at each setting.
            float reach = Mathf.Sin(travel * Mathf.Deg2Rad) * (armLength + 0.012f);
            string[] names = { "IDLE", "25", "50", "75", "FULL" };
            for (int i = 0; i < names.Length; i++)
            {
                float y = Mathf.Lerp(-reach, reach, i / (float)(names.Length - 1));
                SceneKit.Box("Mark", root.transform, new Vector3(0.030f, y, -0.0107f), new Vector3(0.012f, 0.0022f, 0.001f), marking, false, default(Vector3), false);
                AddLabel(names[i], panel, at + new Vector2(0.066f, y), 6.5f, false);
            }

            var lever = root.AddComponent<ThrottleLever>();
            Configure(lever, id, label, handle);
            lever.pivot = pivot.transform;
            lever.idleAngle = -travel;
            lever.fullAngle = travel;
            lever.value = 0f;

            AddLabel(label, panel, at + new Vector2(0f, 0.172f), 9.5f);
            ThrottleLever = lever;
            return lever;
        }

        // ---------------------------------------------------------------- annunciator

        public IndicatorLight Annunciator(Transform panel, Vector2 at, IndicatorId id, string legend, Color color, Vector2 size)
        {
            GameObject root = SceneKit.Empty("Annunciator " + legend, panel, new Vector3(at.x, at.y, 0f));
            SceneKit.Box("Frame", root.transform, new Vector3(0f, 0f, -0.002f), new Vector3(size.x + 0.006f, size.y + 0.006f, 0.004f), bezel, false, default(Vector3), false);
            GameObject window = SceneKit.Box("Lens", root.transform, new Vector3(0f, 0f, -0.0045f), new Vector3(size.x, size.y, 0.002f), lens, false, default(Vector3), false);
            TextMeshPro text = SceneKit.Label(legend, root.transform, new Vector3(0f, 0f, -0.0062f), 6.5f, new Color(0.02f, 0.02f, 0.02f), size.x * 100f);
            text.characterSpacing = 1f;
            return AddIndicator(window, id, color);
        }

        // ---------------------------------------------------------------- shared pieces

        private GameObject ControlRoot(string name, Transform panel, Vector2 at)
        {
            GameObject root = SceneKit.Empty(name, panel, new Vector3(at.x, at.y, 0f));
            root.layer = BuildPaths.LayerInteractable;
            return root;
        }

        private static void AddCollider(GameObject root, Vector3 centre, Vector3 size)
        {
            var collider = root.AddComponent<BoxCollider>();
            collider.center = centre;
            collider.size = size;
        }

        private void Configure(Interactable control, ControlId id, string label, GameObject highlight)
        {
            control.controlId = id;
            control.label = label.Replace('\n', ' ');
            control.settings = assets.Interaction;
            control.highlightRenderers = new[] { highlight.GetComponent<Renderer>() };
            SceneKit.SetLayer(control.gameObject, BuildPaths.LayerInteractable);
            Controls.Add(control);
        }

        private IndicatorLight AddIndicator(GameObject lensObject, IndicatorId id, Color color)
        {
            var indicator = lensObject.AddComponent<IndicatorLight>();
            indicator.indicator = id;
            indicator.spacecraft = spacecraft;
            indicator.theme = assets.Theme;
            indicator.lens = lensObject.GetComponent<Renderer>();
            indicator.litColor = color;
            indicator.unlitColor = LensDark;
            return indicator;
        }

        public TextMeshPro AddLabel(string text, Transform panel, Vector2 at, float emMillimetres, bool backlit = true)
        {
            TextMeshPro label = SceneKit.Label(text, panel, new Vector3(at.x, at.y, -0.0007f), emMillimetres,
                backlit ? LabelColor : new Color(0.55f, 0.58f, 0.60f));
            label.gameObject.layer = BuildPaths.LayerCockpit;
            if (backlit) Labels.Add(label);
            return label;
        }
    }
}
