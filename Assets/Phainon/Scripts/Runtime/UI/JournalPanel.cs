using System.Text;
using Phainon.Simulation;
using TMPro;
using UnityEngine;

namespace Phainon.UI
{
    /// <summary>
    /// The astronaut's log on the HUD, opened with J in a story mission: recovered records,
    /// recordings, what was said on the radio and what was found. Each entry says how far it can be
    /// trusted. Opening the log counts as reading what is in it.
    /// </summary>
    public class JournalPanel : MonoBehaviour
    {
        [SerializeField] internal SpacecraftController spacecraft;
        [SerializeField] internal GameManager game;
        [SerializeField] internal CockpitTheme theme;
        [SerializeField] internal GameObject panel;
        [SerializeField] internal TMP_Text body;

        private readonly StringBuilder builder = new StringBuilder(4096);
        private Journal journal;

        private void Start()
        {
            panel.SetActive(false);
            StoryController story = spacecraft.Craft.Story;
            if (story == null) return;
            journal = story.Journal;
            journal.EntryAdded += entry => { if (panel.activeSelf) Rebuild(); };
        }

        private void Update()
        {
            if (journal == null) return;
            if (game.State != GameState.Flying)
            {
                if (panel.activeSelf) panel.SetActive(false);
                return;
            }
            if (!spacecraft.Input.ToggleJournal.WasPressedThisFrame()) return;
            panel.SetActive(!panel.activeSelf);
            if (panel.activeSelf) Rebuild();
        }

        private void Rebuild()
        {
            builder.Length = 0;
            // What has not been read yet comes first, then the rest, newest first. Routine events stay out of the way at the end.
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = journal.Entries.Count - 1; i >= 0; i--)
                {
                    JournalEntry entry = journal.Entries[i];
                    bool routine = entry.category == (int)JournalCategory.MissionEvent;
                    int wanted = routine ? 2 : (entry.read ? 1 : 0);
                    if (wanted == pass) Append(entry, routine);
                }
            }
            body.text = builder.ToString();
            for (int i = 0; i < journal.Entries.Count; i++) spacecraft.Craft.Story.MarkRead(journal.Entries[i].id);
        }

        private void Append(JournalEntry entry, bool routine)
        {
            string dim = ColorUtility.ToHtmlStringRGB(theme.textDim);
            if (routine)
            {
                builder.Append("<color=#").Append(dim).Append('>').Append(entry.clock).Append("   ").Append(entry.title).Append("</color>\n");
                return;
            }

            Color tag = entry.reliability == (int)JournalReliability.Confirmed ? theme.nominalGreen
                : (entry.reliability == (int)JournalReliability.Unverified ? theme.cautionAmber : theme.warningRed);
            string trust = entry.reliability == (int)JournalReliability.Confirmed ? "CONFIRMED"
                : (entry.reliability == (int)JournalReliability.Unverified ? "UNVERIFIED" : "DAMAGED RECORD");
            builder.Append("<b>").Append(entry.read ? "" : "NEW   ").Append(entry.title.ToUpperInvariant()).Append("</b>   <color=#").Append(dim).Append('>')
                .Append(Category(entry.category)).Append("   ").Append(entry.clock).Append("</color>   <color=#")
                .Append(ColorUtility.ToHtmlStringRGB(tag)).Append('>').Append(trust).Append("</color>\n")
                .Append(entry.body).Append("\n\n");
        }

        private static string Category(int category)
        {
            switch ((JournalCategory)category)
            {
                case JournalCategory.Communication: return "RADIO";
                case JournalCategory.Recording: return "RECORDING";
                case JournalCategory.Discovery: return "DISCOVERY";
                case JournalCategory.Anomaly: return "UNEXPLAINED";
                case JournalCategory.Record: return "SHIP'S RECORD";
                default: return "EVENT";
            }
        }
    }
}
