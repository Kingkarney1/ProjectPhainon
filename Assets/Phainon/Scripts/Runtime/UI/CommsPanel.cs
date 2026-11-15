using System.Text;
using Phainon.Simulation;
using TMPro;
using UnityEngine;

namespace Phainon.UI
{
    /// <summary>
    /// The radio on the HUD: the latest transmission, who sent it, the link status, the replies on
    /// offer and, on request, the message log. It only shows what <see cref="GroundControl"/> reports.
    /// </summary>
    public class CommsPanel : MonoBehaviour
    {
        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal GameManager game;
        [SerializeField] internal CockpitTheme theme;
        [SerializeField] internal CanvasGroup group;
        [SerializeField] internal TMP_Text signalText;
        [SerializeField] internal TMP_Text speakerText;
        [SerializeField] internal TMP_Text stampText;
        [SerializeField] internal TMP_Text bodyText;
        [SerializeField] internal TMP_Text[] replyRows;
        [SerializeField] internal GameObject logPanel;
        [SerializeField] internal TMP_Text logText;
        [Tooltip("How many messages the log shows, newest first.")]
        [SerializeField] internal int logLength = 9;
        [Tooltip("Opacity of the panel when the radio has been quiet for a while.")]
        [SerializeField] internal float restAlpha = 0.85f;
        [SerializeField] internal float attentionSeconds = 14f;

        private readonly StringBuilder builder = new StringBuilder(1024);
        private GroundControl radio;
        private CommsMessage shownPrompt;
        private float sinceMessage = 999f;
        private int shownSignal = int.MinValue;
        private int shownDelay = -1;
        private string shownRequests = "";

        private void Start()
        {
            radio = spacecraft.Craft.GroundControl;
            radio.MessageReceived += Show;
            radio.MessageSent += Show;

            speakerText.text = "";
            stampText.text = "";
            bodyText.text = "The radio is quiet.";
            logPanel.SetActive(false);
            ShowReplies(null);
        }

        private void Show(CommsMessage message)
        {
            bool incoming = message.Direction == CommsDirection.Incoming;
            speakerText.text = message.SpeakerRole + "   " + message.SpeakerName;
            speakerText.color = incoming ? theme.accentBlue : theme.nominalGreen;
            stampText.text = message.Clock;
            bodyText.text = message.Text;
            sinceMessage = 0f;
            if (logPanel.activeSelf) RebuildLog();
        }

        private void Update()
        {
            if (game.State != GameState.Flying) return;
            PhainonInput input = spacecraft.Input;

            if (input.ToggleCommsLog.WasPressedThisFrame())
            {
                logPanel.SetActive(!logPanel.activeSelf);
                if (logPanel.activeSelf) RebuildLog();
            }

            UpdateSignal(spacecraft.Craft.Comms);

            CommsMessage prompt = radio.AwaitingReply;
            if (prompt != shownPrompt) { ShowReplies(prompt); shownRequests = ""; }
            if (prompt != null)
            {
                for (int i = 0; i < input.Replies.Length; i++)
                    if (input.Replies[i].WasPressedThisFrame()) radio.Reply(i);
            }
            else
            {
                UpdateRequests(input);
            }

            sinceMessage += Time.deltaTime;
            bool attention = prompt != null || logPanel.activeSelf || sinceMessage < attentionSeconds || shownRequests.Length > 0;
            group.alpha = Mathf.MoveTowards(group.alpha, attention ? 1f : restAlpha, Time.deltaTime * 2f);
        }

        private void UpdateSignal(CommsSystem comms)
        {
            // -1 no link, -2 acquiring, otherwise signal strength in percent.
            int signal = comms.LinkEstablished ? Mathf.RoundToInt((float)comms.SignalStrength * 100f)
                : (comms.Enabled && comms.AntennaEnabled ? -2 : -1);
            // One-way light time in tenths of a second. It only shows once it is long enough to notice.
            int delay = signal >= 0 ? Mathf.RoundToInt((float)radio.LightDelaySeconds * 10f) : 0;
            if (signal == shownSignal && delay == shownDelay) return;
            shownSignal = signal;
            shownDelay = delay;
            signalText.text = signal >= 0
                ? "LINK  " + signal + "%" + (delay > 0 ? "   DELAY " + (delay / 10f).ToString("0.0") + " s" : "")
                : (signal == -2 ? "ACQUIRING SIGNAL" : "NO LINK");
            signalText.color = signal >= 0 ? theme.nominalGreen : theme.cautionAmber;
        }

        /// <summary>With no question open, the reply keys offer the calls the crew can make to the ground.</summary>
        private void UpdateRequests(PhainonInput input)
        {
            var requests = spacecraft.Craft.Comms.LinkEstablished ? radio.AvailableRequests : null;
            int count = requests != null ? requests.Count : 0;
            string key = count == 0 ? "" : count + requests[0].id;
            if (key != shownRequests)
            {
                shownRequests = key;
                for (int i = 0; i < replyRows.Length; i++)
                {
                    replyRows[i].gameObject.SetActive(i < count);
                    if (i < count) replyRows[i].text = "<b>" + (i + 7) + "</b>   " + requests[i].requestLabel;
                }
            }
            for (int i = 0; i < count && i < input.Replies.Length; i++)
                if (input.Replies[i].WasPressedThisFrame()) { radio.Request(i); shownRequests = "?"; break; }
        }

        private void ShowReplies(CommsMessage prompt)
        {
            shownPrompt = prompt;
            CommsReply[] replies = prompt != null ? prompt.Source.replies : null;
            for (int i = 0; i < replyRows.Length; i++)
            {
                bool used = replies != null && i < replies.Length;
                replyRows[i].gameObject.SetActive(used);
                // Reply keys follow the six view keys: 7, 8 and 9.
                if (used) replyRows[i].text = "<b>" + (i + 7) + "</b>   " + replies[i].label;
            }
        }

        private void RebuildLog()
        {
            string dim = ColorUtility.ToHtmlStringRGB(theme.textDim);
            string ground = ColorUtility.ToHtmlStringRGB(theme.accentBlue);
            string crew = ColorUtility.ToHtmlStringRGB(theme.nominalGreen);

            builder.Length = 0;
            var history = radio.History;
            if (history.Count == 0) builder.Append("No transmissions yet.");
            for (int i = history.Count - 1; i >= 0 && i >= history.Count - logLength; i--)
            {
                CommsMessage message = history[i];
                bool incoming = message.Direction == CommsDirection.Incoming;
                builder.Append("<color=#").Append(dim).Append('>').Append(message.Clock).Append("</color>   <color=#")
                    .Append(incoming ? ground : crew).Append("><b>").Append(incoming ? message.SpeakerRole : message.SpeakerName)
                    .Append("</b></color>   ").Append(message.Text).Append("\n\n");
            }
            logText.text = builder.ToString();
        }
    }
}
