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

        private class Pending
        {
            public CommsLine Line;
            /// <summary>When Ground Control starts transmitting, on this object's clock.</summary>
            public double Due;
        }

        private readonly CommsScriptSpec script;
        private readonly Spacecraft craft;
        private readonly List<Pending> pending = new List<Pending>();
        private readonly List<CommsMessage> history = new List<CommsMessage>();
        private readonly HashSet<CommsLine> delivered = new HashSet<CommsLine>();
        private readonly HashSet<string> flags = new HashSet<string>();

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

        public bool HasFlag(string flag) { return flags.Contains(flag); }

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

        private void Fire(CommsTrigger trigger, WarningId warning = default(WarningId), FaultId fault = default(FaultId))
        {
            // The first words after the link comes up go ahead of anything that was waiting for it.
            bool first = trigger == CommsTrigger.LinkAcquired || trigger == CommsTrigger.LinkRestored;
            for (int i = 0; i < script.lines.Length; i++)
            {
                CommsLine line = script.lines[i];
                if (line.trigger != trigger) continue;
                if (trigger == CommsTrigger.Warning && line.warning != warning) continue;
                if ((trigger == CommsTrigger.Fault || trigger == CommsTrigger.FaultCleared) && line.fault != fault) continue;
                Enqueue(line, clock + line.delaySeconds, first);
            }
        }

        private void Enqueue(CommsLine line, double due, bool first = false)
        {
            if (delivered.Contains(line)) return;
            for (int i = 0; i < pending.Count; i++) if (pending[i].Line == line) return;

            var entry = new Pending { Line = line, Due = due };
            if (first) { pending.Insert(0, entry); return; }
            int at = pending.Count;
            while (at > 0 && pending[at - 1].Due > due) at--;
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
                if (clock < next.Due + LightDelaySeconds) break;       // still on its way; later ones queue behind it
                if (!linked)
                {
                    // Nothing gets through. Ground keeps trying for a while, then gives the line up.
                    if (clock - next.Due > script.staleAfterSeconds) pending.RemoveAt(i); else i++;
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
                default: return flags.Contains(flag);
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
                .Replace("{window}", Whole(craft.Mission.Spec.ignitionWindowSeconds));
        }

        private static string Kilometres(double meters)
        {
            return double.IsInfinity(meters) || double.IsNaN(meters) ? "--" : Whole(meters / 1000.0);
        }

        private static string Whole(double value) { return value.ToString("0", CultureInfo.InvariantCulture); }

        // ---------------------------------------------------------------- crew replies

        /// <summary>Transmits one of the offered replies to <see cref="AwaitingReply"/>.</summary>
        public CommandResult Reply(int index)
        {
            if (AwaitingReply == null) return CommandResult.Rejected("NO TRANSMISSION IS AWAITING A REPLY");
            CommsReply[] replies = AwaitingReply.Source.replies;
            if (index < 0 || index >= replies.Length) return CommandResult.Rejected("NO SUCH REPLY");
            if (!craft.Comms.LinkEstablished) return CommandResult.Rejected("NO LINK - CANNOT TRANSMIT");

            CommsReply reply = replies[index];
            var message = new CommsMessage
            {
                Clock = craft.Mission.FormatClock(),
                SpeakerName = CrewName,
                SpeakerRole = "CREW",
                Text = reply.line,
                Direction = CommsDirection.Outgoing
            };
            history.Add(message);
            AwaitingReply = null;
            if (!string.IsNullOrEmpty(reply.setsFlag)) flags.Add(reply.setsFlag);

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
            if (MessageSent != null) MessageSent(message);
            return CommandResult.Ok(null);
        }

        private void ExpireReply()
        {
            // Silence is remembered too: lines can require "<line id>_no_reply".
            flags.Add(AwaitingReply.Source.id + "_no_reply");
            AwaitingReply = null;
            Fire(CommsTrigger.ReplyTimeout);
        }
    }
}
