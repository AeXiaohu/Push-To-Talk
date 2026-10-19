using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace PushToTalk
{
    public enum ControlMode { PushToTalk, PushToMute, Toggle }

    // Pure state machine: bindings combine with OR, keys within a binding with AND.
    public sealed class MuteStateMachine
    {
        private readonly ControlMode mode;
        private readonly List<HotkeyBinding> bindings;
        private HashSet<string> previous = new HashSet<string>();
        private bool toggleMute;
        private bool waitingForRelease = true;

        public MuteStateMachine(ControlMode mode, IEnumerable<HotkeyBinding> bindings, bool originalMute)
        {
            this.mode = mode;
            this.bindings = new List<HotkeyBinding>(bindings);
            toggleMute = originalMute;
        }

        public bool WaitingForRelease { get { return waitingForRelease; } }

        public void RequireRelease()
        {
            waitingForRelease = true;
            previous.Clear();
        }

        public bool Update(ISet<Keys> pressed)
        {
            HashSet<string> current = new HashSet<string>();
            foreach (HotkeyBinding binding in bindings)
                if (binding.IsDown(pressed)) current.Add(binding.Serialize());

            // A key already held when enabling/resuming must not open the mic.
            if (waitingForRelease)
            {
                if (current.Count == 0) waitingForRelease = false;
                previous = current;
                return true;
            }

            if (mode == ControlMode.PushToTalk) return current.Count == 0;
            if (mode == ControlMode.PushToMute) return current.Count != 0;

            // Treat overlapping alternatives as one hold, including F2 + Ctrl+F2.
            bool rising = current.Count > 0 && previous.Count == 0;
            if (rising) toggleMute = !toggleMute;
            previous = current;
            return toggleMute;
        }
    }
}
