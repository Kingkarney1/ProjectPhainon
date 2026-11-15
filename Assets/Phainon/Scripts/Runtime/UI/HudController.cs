using Phainon.Cockpit;
using Phainon.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Phainon.UI
{
    /// <summary>
    /// The thin layer of on-screen help over the cockpit view: the interaction prompt for whatever
    /// control is under the pointer, the current objective, short notifications from the flight
    /// computer and the active warning. Everything here mirrors information that also exists on the
    /// cockpit displays; it is an accessibility aid, not a replacement for them.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        private struct Toast
        {
            public string text;
            public Color color;
            public float age;
        }

        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal GameManager game;
        [SerializeField] internal CockpitInteractionManager interaction;
        [SerializeField] internal CockpitControlRouter router;
        [SerializeField] internal CockpitCameraController cockpitCamera;
        [SerializeField] internal CockpitTheme theme;

        [SerializeField] internal CanvasGroup root;
        [Header("Objective guide")]
        [SerializeField] internal GameObject guidePanel;
        [SerializeField] internal TMP_Text phaseTitle;
        [SerializeField] internal TMP_Text objectiveText;
        [SerializeField] internal TMP_Text clockText;
        [Header("Interaction prompt")]
        [SerializeField] internal CanvasGroup promptGroup;
        [SerializeField] internal TMP_Text promptTitle;
        [SerializeField] internal TMP_Text promptAction;
        [SerializeField] internal Image reticle;
        [Header("Notifications")]
        [SerializeField] internal TMP_Text[] toastRows;
        [SerializeField] internal float toastSeconds = 4.5f;
        [Header("Warning banner")]
        [SerializeField] internal CanvasGroup warningGroup;
        [SerializeField] internal Image warningBackground;
        [SerializeField] internal TMP_Text warningText;

        private Toast[] toasts;
        private int toastCount;
        private float throttleFlash;
        private string lastObjective;
        private string lastTitle;
        private int lastRate;

        private void Awake()
        {
            toasts = new Toast[toastRows.Length];
            root.alpha = 0f;
        }

        private void Start()
        {
            Spacecraft craft = spacecraft.Craft;
            craft.Mission.Notification += message => Push(message, theme.accentBlue);
            craft.Warnings.Raised += entry => Push(entry.Message, theme.ForSeverity(entry.Severity));
            craft.CommandHandled += OnCommandHandled;
            router.CommandRejected += (id, message) => Push(message, theme.cautionAmber);
            spacecraft.TimeRateNotice += message => Push(message, theme.accentBlue);
            spacecraft.SaveNotice += message => Push(message, theme.accentBlue);
            craft.Science.ObservationCompleted += (record, observation) =>
                Push("OBSERVATION COMPLETE - QUALITY " + record.quality.ToString("0"), theme.nominalGreen);
            craft.Science.ObservationAbandoned += observation => Push("OBSERVATION LOST - TARGET OUT OF VIEW", theme.cautionAmber);
            if (craft.Story != null)
                craft.Story.Journal.EntryAdded += entry =>
                {
                    if (entry.category != (int)JournalCategory.MissionEvent) Push("NEW IN THE LOG - " + entry.title.ToUpperInvariant() + "   (J)", theme.accentBlue);
                };
        }

        private void OnCommandHandled(ControlId id, CommandResult result)
        {
            if (id == ControlId.Throttle) throttleFlash = 1.4f;
        }

        private void Push(string message, Color color)
        {
            if (string.IsNullOrEmpty(message)) return;

            // Newest message goes on top; older ones shuffle down and the oldest falls off.
            for (int i = Mathf.Min(toastCount, toasts.Length - 1); i > 0; i--) toasts[i] = toasts[i - 1];
            toasts[0] = new Toast { text = message, color = color, age = 0f };
            toastCount = Mathf.Min(toastCount + 1, toasts.Length);
        }

        private void Update()
        {
            bool visible = game.State == GameState.Flying;
            root.alpha = Mathf.MoveTowards(root.alpha, visible ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            if (!visible) return;

            Spacecraft craft = spacecraft.Craft;
            UpdateGuide(craft);
            UpdatePrompt(craft);
            UpdateToasts();
            UpdateWarning(craft);
        }

        private void UpdateGuide(Spacecraft craft)
        {
            bool show = router.GuideEnabled;
            if (guidePanel.activeSelf != show) guidePanel.SetActive(show);
            if (!show) return;

            MissionController mission = craft.Mission;
            string title = mission.PhaseTitle;
            int rate = spacecraft.TimeRate;
            if (title != lastTitle || rate != lastRate)
            {
                lastTitle = title;
                lastRate = rate;
                phaseTitle.text = rate > 1 ? title + "   TIME x" + rate : title;
            }
            phaseTitle.color = mission.IsFailed ? theme.warningRed : (mission.IsAccomplished ? theme.nominalGreen : theme.accentBlue);
            clockText.text = mission.FormatClock();

            string objective = mission.CurrentObjective;
            if (objective != lastObjective)
            {
                lastObjective = objective;
                objectiveText.text = objective;
            }
        }

        private void UpdatePrompt(Spacecraft craft)
        {
            Interactable hovered = interaction.Active != null ? interaction.Active : interaction.Hovered;
            throttleFlash -= Time.unscaledDeltaTime;

            float targetAlpha = 0f;
            if (hovered != null)
            {
                string state = hovered.StateText;
                promptTitle.text = string.IsNullOrEmpty(state) ? hovered.Label : hovered.Label + "   <color=#4FD37A>" + state + "</color>";
                promptAction.text = hovered.Prompt;
                targetAlpha = 1f;
            }
            else if (throttleFlash > 0f)
            {
                // Keyboard throttle changes happen without looking at the lever, so echo the setting briefly.
                promptTitle.text = "THROTTLE   <color=#4FD37A>" + (craft.Engine.ThrottleCommand * 100.0).ToString("0") + "%</color>";
                promptAction.text = craft.Engine.ThrustLimit < craft.Engine.ThrottleCommand - 0.005
                    ? "Limited to " + (craft.Engine.ThrustLimit * 100.0).ToString("0") + "% by the THRUST LIMIT dial"
                    : "Shift / Ctrl to adjust";
                targetAlpha = 1f;
            }
            promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, targetAlpha, Time.unscaledDeltaTime * 8f);

            bool looking = cockpitCamera.IsLooking;
            if (reticle.enabled != looking) reticle.enabled = looking;
            if (looking) reticle.color = hovered != null ? theme.accentBlue : theme.textDim;
        }

        private void UpdateToasts()
        {
            for (int i = 0; i < toastRows.Length; i++)
            {
                if (i >= toastCount)
                {
                    if (toastRows[i].text.Length > 0) toastRows[i].text = string.Empty;
                    continue;
                }

                toasts[i].age += Time.unscaledDeltaTime;
                float remaining = toastSeconds - toasts[i].age;
                if (remaining <= 0f)
                {
                    toastCount = i;      // everything below this row is older still
                    toastRows[i].text = string.Empty;
                    continue;
                }

                Color color = toasts[i].color;
                color.a = Mathf.Clamp01(remaining / 0.8f) * (1f - i * 0.18f);
                toastRows[i].text = toasts[i].text;
                toastRows[i].color = color;
            }
        }

        private void UpdateWarning(Spacecraft craft)
        {
            WarningSystem warnings = craft.Warnings;
            bool show = craft.Power.IsBusPowered && warnings.AnyWarning && warnings.Active.Count > 0;
            warningGroup.alpha = Mathf.MoveTowards(warningGroup.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            if (!show) return;

            WarningEntry top = warnings.Active[0];
            warningText.text = warnings.MasterAlarmActive
                ? top.Message + "\n<size=62%>PRESS MASTER ALARM TO ACKNOWLEDGE</size>"
                : top.Message;

            bool bright = !warnings.MasterAlarmActive || theme.BlinkPhase(Time.unscaledTime);
            Color background = theme.warningRed;
            background.a = bright ? 0.85f : 0.35f;
            warningBackground.color = background;
        }
    }
}
