using System;
using System.Collections.Generic;

namespace Phainon.Simulation
{
    /// <summary>
    /// The astronaut's log: what happened, what was said, what was found and what was recovered
    /// from the ship's records. Entries keep how far each one can be trusted.
    /// </summary>
    public class Journal
    {
        private readonly List<JournalEntry> entries = new List<JournalEntry>();

        /// <summary>Oldest first.</summary>
        public IReadOnlyList<JournalEntry> Entries { get { return entries; } }
        public event Action<JournalEntry> EntryAdded;
        public event Action<JournalEntry> EntryRead;

        public int UnreadCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < entries.Count; i++) if (!entries[i].read) n++;
                return n;
            }
        }

        public JournalEntry Find(string id)
        {
            for (int i = 0; i < entries.Count; i++) if (entries[i].id == id) return entries[i];
            return null;
        }

        public JournalEntry Add(string id, JournalCategory category, JournalReliability reliability, string title, string body, string clock)
        {
            JournalEntry existing = Find(id);
            if (existing != null) return existing;
            var entry = new JournalEntry
            {
                id = id, category = (int)category, reliability = (int)reliability, title = title, body = body, clock = clock
            };
            entries.Add(entry);
            if (EntryAdded != null) EntryAdded(entry);
            return entry;
        }

        /// <summary>Marks an entry as opened by the crew.</summary>
        public void MarkRead(string id)
        {
            JournalEntry entry = Find(id);
            if (entry == null || entry.read) return;
            entry.read = true;
            if (EntryRead != null) EntryRead(entry);
        }

        public JournalEntry[] Capture() { return entries.ToArray(); }

        public void Restore(JournalEntry[] saved)
        {
            entries.Clear();
            if (saved != null) entries.AddRange(saved);
        }
    }
}
