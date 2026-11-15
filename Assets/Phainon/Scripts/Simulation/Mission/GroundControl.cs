using System;
using System.Collections.Generic;
using System.Globalization;

namespace Phainon.Simulation
{
    public enum CommsDirection { Incoming, Outgoing }

    /// <summary>One entry in the radio log.</summary>
    public class CommsMessage
    {
        /// <summary>Mission clock when the message was heard or sent.</summary>
        public string Clock;
        public string SpeakerName;
        public string SpeakerRole;
        public string Text;
        public CommsDirection Direction;
        /// <summary>The authored line behind an incoming message. Null for crew transmissions.</summary>
        public CommsLine Source;
    }

    [Serializable]
    public class CommsMessageSnapshot
    {
        public string clock, speakerName, speakerRole, text;
        public bool incoming;
        /// <summary>Index of the authored line in the script, or -1 for a crew transmission.</summary>
        public int line = -1;
    }

    [Serializable]
    public class GroundControlSnapshot
    {
        public double clock, nextSlot, replyDeadline;
        public bool hadLink, wasAllGo;
        public int lastLaunchState;
        public int[] delivered = new int[0];
        public int[] pendingLines = new int[0];
        public double[] pendingDue = new double[0];
        public bool[] pendingPriority = new bool[0];
        public CommsMessageSnapshot[] history = new CommsMessageSnapshot[0];
        /// <summary>Index in the history of the question still open, or -1.</summary>
        public int awaiting = -1;
    }

    /// <summary>
    /// Ground Control's side of the radio. It listens to the spacecraft's own events, never to a
    /// timer or a copy of the state, and transmits the authored lines that match. A line only
    /// reaches the crew while the radio link is up and after the signal's travel time.
    /// </summary>
    public class GroundControl
    {
        public const double SpeedOfLight = 299792458.0;     // m/s
        public const string CrewName = "PHAINON";
        /// <summary>Shortest pause between two transmissions, so they do not talk over each other.</summary>
        private const double MinimumGapSeconds = 3.0;
        /// <summary>Most calls offered to the crew at once: one per reply key.</summary>
        public const int MaxRequests = 3;

        private class Pending
        {
            public CommsLine Line;
            /// <summary>When Ground Control starts transmitting, on this object's clock.</summary>
            public double Due;
            /// <summary>True for the first words after the link comes up, which nothing else may overtake.</summary>
            public bool Priority;
        }

        private readonly CommsScriptSpec script;
        private readonly Spacecraft craft;
        private readonly List<Pending> pending = new List<Pending>();
        private readonly List<CommsMessage> history = new List<CommsMessage>();
        private readonly HashSet<CommsLine> delivered = new HashSet<CommsLine>();
        private readonly List<CommsLine> requests = new List<CommsLine>();

        private double clock;
        private double nextSlot;
        private double replyDeadline;
        private bool hadLink;
        private bool wasAllGo;
        private LaunchSequenceState lastLaunchState;

        public GroundControl(CommsScriptSpec script, Spacecraft craft)
        {
            // An empty script would leave the radio silent with no clue why; fall back to the built-in one.
            this.script = script != null && script.lines != null && script.lines.Length > 0 ? script : new CommsScriptSpec();
            this.craft = craft;

            craft.Mission.EventReached += OnMissionEvent;
            craft.Mission.Failed += reason => Fire(CommsTrigger.MissionFailed);
            craft.Comms.LinkChanged += OnLinkChanged;
            craft.Navigation.AlignmentComplete += () => Fire(CommsTrigger.NavAligned);
            craft.Engine.Ignited += stage => { if (stage == 0) Fire(CommsTrigger.MainEngineIgnition); };
            craft.Warnings.Raised += entry => Fire(CommsTrigger.Warning, entry.Id);
            craft.Faults.Raised += id => Fire(CommsTrigger.Fault, default(WarningId), id);
            craft.Faults.Cleared += id => Fire(CommsTrigger.FaultCleared, default(WarningId), id);
        }

        public CommsScriptSpec Script { get { return script; } }
        /// <summary>Everything heard and sent so far, oldest first.</summary>
        public IReadOnlyList<CommsMessage> History { get { return history; } }
        /// <summary>The transmission the crew have been asked to answer, or null.</summary>
        public CommsMessage AwaitingReply { get; private set; }
        /// <summary>Seconds left to answer <see cref="AwaitingReply"/>.</summary>
        public double ReplySecondsRemaining { get { return AwaitingReply != null ? Math.Max(0.0, replyDeadline - clock) : 0.0; } }
        /// <summary>One-way signal travel time to the ground station, in seconds.</summary>
        public double LightDelaySeconds { get; private set; }

        public event Action<CommsMessage> MessageReceived;
        public event Action<CommsMessage> MessageSent;

        public bool HasFlag(string flag) { return craft.Flags.Contains(flag); }

        // ---------------------------------------------------------------- simulation events

        private void OnMissionEvent(MissionEvent e)
        {
            switch (e.Id)
            {
                case MissionEventId.Liftoff: Fire(CommsTrigger.Liftoff); break;
                case MissionEventId.MaxQ: Fire(CommsTrigger.MaxQ); break;
                case MissionEventId.BoosterCutoff: Fire(CommsTrigger.BoosterCutoff); break;
                case MissionEventId.StageSeparation: Fire(CommsTrigger.StageSeparation); break;
                case MissionEventId.UpperStageIgnition: Fire(CommsTrigger.UpperStageIgnition); break;
                case MissionEventId.OrbitGo: Fire(CommsTrigger.OrbitGo); break;
                case MissionEventId.OrbitAchieved:
                    Fire(CommsTrigger.OrbitAchieved);
                    Fire(CommsTrigger.OrbitTimer);
                    break;
                case MissionEventId.UpperStageJettison: Fire(CommsTrigger.UpperStageJettison); break;
                case MissionEventId.DepartureBurn: Fire(CommsTrigger.DepartureBurn); break;
                case MissionEventId.EarthEscape: Fire(CommsTrigger.EarthEscape); break;
                case MissionEventId.DistanceMark: Fire(CommsTrigger.DistanceMark); break;
            }
        }

        private void OnLinkChanged(bool linked)
        {
            if (!linked) return;
            Fire(hadLink ? CommsTrigger.LinkRestored : CommsTrigger.LinkAcquired);
            hadLink = true;
        }

        /// <summary>Something happened in the story that Ground Control may have a line for.</summary>
        public void NotifyStoryEvent(string eventId) { Fire(CommsTrigger.StoryEvent, default(WarningId), default(FaultId), eventId); }

        /// <summary>Conditions that have no event of their own are watched for a change each tick.</summary>
        private void PollMissionState()
        {
            MissionController mission = craft.Mission;
            bool allGo = mission.Phase == MissionPhase.PreLaunch && mission.AllGo;
            if (allGo && !wasAllGo && mission.LaunchState == LaunchSequenceState.Idle) Fire(CommsTrigger.AllStationsGo);
            wasAllGo = allGo;

            LaunchSequenceState state = mission.LaunchState;
            if (state == lastLaunchState) return;
            if (state == LaunchSequenceState.Counting && lastLaunchState == LaunchSequenceState.Idle) Fire(CommsTrigger.CountdownStarted);
            else if (state == LaunchSequenceState.Hold) Fire(CommsTrigger.CountdownHold);
            lastLaunchState = state;
        }

        private void Fire(CommsTrigger trigger, WarningId warning = default(WarningId), FaultId fault = default(FaultId), string storyEvent = null)
        {
            // The first words after the link comes up go ahead of anything that was waiting for it.
            bool first = trigger == CommsTrigger.LinkAcquired || trigger == CommsTrigger.LinkRestored;
            // Far from home the ground only sees what happened once the telemetry has crossed the distance.
            double lag = script.groundReactsAfterLightTime ? LightDelaySeconds : 0.0;
            for (int i = 0; i < script.lines.Length; i++)
            {
                CommsLine line = script.lines[i];
                if (line.trigger != trigger) continue;
                if (trigger == CommsTrigger.Warning && line.warning != warning) continue;
                if ((trigger == CommsTrigger.Fault || trigger == CommsTrigger.FaultCleared) && line.fault != fault) continue;
                if (trigger == CommsTrigger.StoryEvent && line.storyEvent != storyEvent) continue;
                Enqueue(line, clock + (line.alreadyOnItsWay ? -LightDelaySeconds : lag) + line.delaySeconds, first);
            }
        }

        private void Enqueue(CommsLine line, double due, bool first = false)
        {
            if (delivered.Contains(line)) return;
            for (int i = 0; i < pending.Count; i++) if (pending[i].Line == line) return;

            var entry = new Pending { Line = line, Due = due, Priority = first };
            int at;
            if (first)
            {
                // Ahead of everything that was waiting for the link, in the order they will arrive.
                at = 0;
                while (at < pending.Count && pending[at].Priority && pending[at].Due <= due) at++;
            }
            else
            {
                at = pending.Count;
                while (at > 0 && !pending[at - 1].Priority && pending[at - 1].Due > due) at--;
            }
            pending.Insert(at, entry);
        }

        // ---------------------------------------------------------------- time step

        /// <summary>
        /// Advances the radio. The distance is the signal path to the ground station; in low orbit
        /// the delay it causes is milliseconds, but nothing here assumes it stays that small.
        /// </summary>
        public void Tick(double dt, double distanceToStationMeters)
        {
            clock += dt;
            LightDelaySeconds = Math.Max(0.0, distanceToStationMeters) / SpeedOfLight;
            PollMissionState();

            if (AwaitingReply != null && clock >= replyDeadline) ExpireReply();

            bool linked = craft.Comms.LinkEstablished;
            int i = 0;
            while (i < pending.Count)
            {
                Pending next = pending[i];
                if (clock < next.Due + LightDelaySeconds)
                {
                    // Still on its way. The greeting after a link-up holds back what queued behind it; nothing else does.
                    if (next.Priority) break;
                    i++;
                    continue;
                }
                if (!linked)
                {
                    // Nothing gets through. Ground keeps trying for a while, then gives the line up.
                    if (clock - next.Due - LightDelaySeconds > script.staleAfterSeconds) pending.RemoveAt(i); else i++;
                    continue;
                }
                if (clock < nextSlot) break;

                pending.RemoveAt(i);
                // Conditions are judged at the moment of speaking, against the spacecraft as it is now.
                if (!ConditionsHold(next.Line)) continue;
                Deliver(next.Line);
                break;
            }
        }

        private bool ConditionsHold(CommsLine line)
        {
            // Advice about a fault the crew have already dealt with would contradict their instruments.
            if (line.trigger == CommsTrigger.Fault && !craft.Faults.IsActive(line.fault)) return false;
            if (!string.IsNullOrEmpty(line.requiresFlag))
                foreach (string flag in line.requiresFlag.Split(' ')) if (flag.Length > 0 && !Test(flag)) return false;
            if (!string.IsNullOrEmpty(line.forbidsFlag))
                foreach (string flag in line.forbidsFlag.Split(' ')) if (flag.Length > 0 && Test(flag)) return false;
            return true;
        }

        /// <summary>Story flags are remembered; names starting with @ are read from the spacecraft as it is now.</summary>
        private bool Test(string flag)
        {
            if (flag.StartsWith("@km:", StringComparison.Ordinal))
                return craft.Flight.AltitudeMeters >= double.Parse(flag.Substring(4), CultureInfo.InvariantCulture) * 1000.0;
            switch (flag)
            {
                case "@guidance_active": return craft.Navigation.GuidanceActive;
                case "@counting": return craft.Mission.LaunchState != LaunchSequenceState.Idle;
                case "@lifted_off": return craft.Mission.HasLiftedOff;
                case "@upper_stage": return craft.Engine.ActiveStage > 0;
                case "@burning": return craft.Engine.IsBurning;
                case "@in_orbit": return craft.Mission.IsComplete;
                case "@escaped": return craft.Mission.HasEscaped;
                case "@bound": return craft.Flight.IsBound;
                case "@orbit_stable": return craft.Story != null && craft.Story.OrbitStable;
                case "@apu_online": return craft.Power.ApuOnline;
                default: return craft.Flags.Contains(flag);
            }
        }

        private void Deliver(CommsLine line)
        {
            delivered.Add(line);
            CommsCharacter speaker = FindCharacter(line.speaker);
            var message = new CommsMessage
            {
                Clock = craft.Mission.FormatClock(),
                SpeakerName = speaker != null ? speaker.name : line.speaker,
                SpeakerRole = speaker != null ? speaker.role : "GROUND",
                Text = FillTelemetry(line.text),
                Direction = CommsDirection.Incoming,
                Source = line
            };
            history.Add(message);
            nextSlot = clock + MinimumGapSeconds;
            if (!string.IsNullOrEmpty(line.setsFlag)) craft.Flags.Add(line.setsFlag);

            if (line.replies != null && line.replies.Length > 0)
            {
                // A new question replaces one the crew never answered.
                if (AwaitingReply != null) ExpireReply();
                AwaitingReply = message;
                replyDeadline = clock + script.replyWindowSeconds;
            }
            if (MessageReceived != null) MessageReceived(message);
        }

        private CommsCharacter FindCharacter(string id)
        {
            if (script.characters == null) return null;
            for (int i = 0; i < script.characters.Length; i++)
                if (script.characters[i].id == id) return script.characters[i];
            return null;
        }

        /// <summary>Replaces telemetry tags with the spacecraft's current values.</summary>
        private string FillTelemetry(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
            FlightModel flight = craft.Flight;
            return text
                .Replace("{alt}", Kilometres(flight.AltitudeMeters))
                .Replace("{vel}", Whole(flight.Speed))
                .Replace("{fuel}", Whole(craft.Fuel.GetFraction(craft.Engine.ActiveStage) * 100.0))
                .Replace("{thrust}", Whole(craft.Engine.OutputFraction * 100.0))
                .Replace("{apo}", Kilometres(flight.ApoapsisMeters))
                .Replace("{peri}", Kilometres(flight.PeriapsisMeters))
                .Replace("{dist}", (flight.AltitudeMeters / 1000.0).ToString("N0", CultureInfo.InvariantCulture))
                .Replace("{delay}", LightDelaySeconds.ToString("0.0", CultureInfo.InvariantCulture))
                .Replace("{delaymin}", Whole(LightDelaySeconds / 60.0))
                .Replace("{dv}", Whole(craft.AvailableDeltaV))
                .Replace("{capture}", Whole(craft.Story != null ? craft.Story.CaptureDeltaV : 0.0))
                .Replace("{oxygen}", Whole(craft.LifeSupport.OxygenFraction * 100.0))
                .Replace("{window}", Whole(craft.Mission.Spec.ignitionWindowSeconds));
        }

        private static string Kilometres(double meters)
        {
            return double.IsInfinity(meters) || double.IsNaN(meters) ? "--" : Whole(meters / 1000.0);
        }

        private static string Whole(double value) { return value.ToString("0", CultureInfo.InvariantCulture); }

        // ---------------------------------------------------------------- crew replies and calls

        /// <summary>Transmits one of the offered replies to <see cref="AwaitingReply"/>.</summary>
        public CommandResult Reply(int index)
        {
            if (AwaitingReply == null) return CommandResult.Rejected("NO TRANSMISSION IS AWAITING A REPLY");
            CommsReply[] replies = AwaitingReply.Source.replies;
            if (index < 0 || index >= replies.Length) return CommandResult.Rejected("NO SUCH REPLY");
            if (!craft.Comms.LinkEstablished) return CommandResult.Rejected("NO LINK - CANNOT TRANSMIT");

            CommsReply reply = replies[index];
            Transmit(reply.line);
            AwaitingReply = null;
            if (!string.IsNullOrEmpty(reply.setsFlag)) craft.Flags.Add(reply.setsFlag);

            if (!string.IsNullOrEmpty(reply.followUp))
            {
                // The crew's words take the light delay to reach the ground; the answer takes it again
                // on the way back, which Tick adds on arrival.
                for (int i = 0; i < script.lines.Length; i++)
                {
                    CommsLine line = script.lines[i];
                    if (line.id == reply.followUp) Enqueue(line, clock + LightDelaySeconds + line.delaySeconds);
                }
            }
            return CommandResult.Ok(null);
        }

        /// <summary>
        /// Calls the crew can make to the ground right now: lines authored as requests whose
        /// conditions hold and that have not been asked yet. At most <see cref="MaxRequests"/>.
        /// </summary>
        public IReadOnlyList<CommsLine> AvailableRequests
        {
            get
            {
                requests.Clear();
                for (int i = 0; i < script.lines.Length && requests.Count < MaxRequests; i++)
                {
                    CommsLine line = script.lines[i];
                    if (line.trigger != CommsTrigger.CrewRequest || delivered.Contains(line) || IsPending(line)) continue;
                    if (ConditionsHold(line)) requests.Add(line);
                }
                return requests;
            }
        }

        /// <summary>Makes one of the <see cref="AvailableRequests"/>. The answer comes back after the round trip.</summary>
        public CommandResult Request(int index)
        {
            IReadOnlyList<CommsLine> available = AvailableRequests;
            if (index < 0 || index >= available.Count) return CommandResult.Rejected("NO SUCH CALL");
            if (!craft.Comms.LinkEstablished) return CommandResult.Rejected("NO LINK - CANNOT TRANSMIT");
            CommsLine line = available[index];
            Transmit(line.requestLine);
            Enqueue(line, clock + LightDelaySeconds + line.delaySeconds);
            return CommandResult.Ok(null);
        }

        private bool IsPending(CommsLine line)
        {
            for (int i = 0; i < pending.Count; i++) if (pending[i].Line == line) return true;
            return false;
        }

        private void Transmit(string text)
        {
            var message = new CommsMessage
            {
                Clock = craft.Mission.FormatClock(),
                SpeakerName = CrewName,
                SpeakerRole = "CREW",
                Text = text,
                Direction = CommsDirection.Outgoing
            };
            history.Add(message);
            if (MessageSent != null) MessageSent(message);
        }

        private void ExpireReply()
        {
            // Silence is remembered too: lines can require "<line id>_no_reply".
            craft.Flags.Add(AwaitingReply.Source.id + "_no_reply");
            AwaitingReply = null;
            Fire(CommsTrigger.ReplyTimeout);
        }

        // ---------------------------------------------------------------- save state

        private int IndexOf(CommsLine line) { return line == null ? -1 : Array.IndexOf(script.lines, line); }
        private CommsLine LineAt(int index) { return index >= 0 && index < script.lines.Length ? script.lines[index] : null; }

        public GroundControlSnapshot Capture()
        {
            var s = new GroundControlSnapshot
            {
                clock = clock, nextSlot = nextSlot, replyDeadline = replyDeadline, hadLink = hadLink, wasAllGo = wasAllGo,
                lastLaunchState = (int)lastLaunchState
            };
            var deliveredLines = new List<int>();
            foreach (CommsLine line in delivered) deliveredLines.Add(IndexOf(line));
            deliveredLines.Sort();
            s.delivered = deliveredLines.ToArray();

            s.pendingLines = new int[pending.Count];
            s.pendingDue = new double[pending.Count];
            s.pendingPriority = new bool[pending.Count];
            for (int i = 0; i < pending.Count; i++)
            {
                s.pendingLines[i] = IndexOf(pending[i].Line);
                s.pendingDue[i] = pending[i].Due;
                s.pendingPriority[i] = pending[i].Priority;
            }

            s.history = new CommsMessageSnapshot[history.Count];
            for (int i = 0; i < history.Count; i++)
            {
                CommsMessage m = history[i];
                s.history[i] = new CommsMessageSnapshot
                {
                    clock = m.Clock, speakerName = m.SpeakerName, speakerRole = m.SpeakerRole, text = m.Text,
                    incoming = m.Direction == CommsDirection.Incoming, line = IndexOf(m.Source)
                };
                if (m == AwaitingReply) s.awaiting = i;
            }
            return s;
        }

        public void Restore(GroundControlSnapshot s)
        {
            clock = s.clock;
            nextSlot = s.nextSlot;
            replyDeadline = s.replyDeadline;
            hadLink = s.hadLink;
            wasAllGo = s.wasAllGo;
            lastLaunchState = (LaunchSequenceState)s.lastLaunchState;

            delivered.Clear();
            for (int i = 0; i < s.delivered.Length; i++)
            {
                CommsLine line = LineAt(s.delivered[i]);
                if (line != null) delivered.Add(line);
            }

            pending.Clear();
            for (int i = 0; i < s.pendingLines.Length && i < s.pendingDue.Length; i++)
            {
                CommsLine line = LineAt(s.pendingLines[i]);
                bool priority = s.pendingPriority != null && i < s.pendingPriority.Length && s.pendingPriority[i];
                if (line != null) pending.Add(new Pending { Line = line, Due = s.pendingDue[i], Priority = priority });
            }

            history.Clear();
            AwaitingReply = null;
            for (int i = 0; i < s.history.Length; i++)
            {
                CommsMessageSnapshot m = s.history[i];
                var message = new CommsMessage
                {
                    Clock = m.clock, SpeakerName = m.speakerName, SpeakerRole = m.speakerRole, Text = m.text,
                    Direction = m.incoming ? CommsDirection.Incoming : CommsDirection.Outgoing, Source = LineAt(m.line)
                };
                history.Add(message);
                if (i == s.awaiting && message.Source != null && message.Source.replies != null) AwaitingReply = message;
            }
        }
    }
}
