namespace Phainon.Simulation
{
    /// <summary>Outcome of a crew command, with a short message suitable for the cockpit annunciator.</summary>
    public struct CommandResult
    {
        public bool Accepted;
        public string Message;

        public static CommandResult Ok(string message)
        {
            return new CommandResult { Accepted = true, Message = message };
        }

        public static CommandResult Rejected(string message)
        {
            return new CommandResult { Accepted = false, Message = message };
        }
    }
}
