
// Written by:
// 
// ███╗   ██╗ ██████╗  ██████╗██╗  ██╗ █████╗ ██╗      █████╗ 
// ████╗  ██║██╔═══██╗██╔════╝██║  ██║██╔══██╗██║     ██╔══██╗
// ██╔██╗ ██║██║   ██║██║     ███████║███████║██║     ███████║
// ██║╚██╗██║██║   ██║██║     ██╔══██║██╔══██║██║     ██╔══██║
// ██║ ╚████║╚██████╔╝╚██████╗██║  ██║██║  ██║███████╗██║  ██║
// ╚═╝  ╚═══╝ ╚═════╝  ╚═════╝╚═╝  ╚═╝╚═╝  ╚═╝╚══════╝╚═╝  ╚═╝
//
//                    N O C H A L A


using System;
using GTA;

namespace EngineStateManager
{
    internal enum EngineIntent
    {
        None = 0,
        ForceOff = 1,
        ForceOn = 2
    }

    internal enum EngineIntentPriority
    {
        // Higher wins
        Low = 10,
        Normal = 50,
        High = 90,
        Critical = 100,
    }

    internal static class EngineOverrideBus
    {
        private struct IntentState
        {
            public EngineIntent Intent;
            public EngineIntentPriority Priority;
            public int ExpiresAt;
            public string Owner;
        }

        private static IntentState _state;

        public static void Set(EngineIntent intent, EngineIntentPriority priority, int durationMs, string owner)
        {
            int now = Game.GameTime;
            int expires = durationMs <= 0 ? int.MaxValue : checked(now + durationMs);

            // Replace only if higher priority OR expired OR same owner
            bool expired = now >= _state.ExpiresAt;
            bool replace =
                expired ||
                owner == _state.Owner ||
                priority >= _state.Priority;

            if (!replace) return;

            _state.Intent = intent;
            _state.Priority = priority;
            _state.ExpiresAt = expires;
            _state.Owner = owner;

            if (ModLogger.Enabled)
                ModLogger.Info($"EngineOverrideBus.Set: Intent={intent}, Pri={priority}, DurMs={durationMs}, Owner={owner}");
        }

        public static void Clear(string owner)
        {
            if (_state.Owner != owner) return;

            _state.Intent = EngineIntent.None;
            _state.Priority = EngineIntentPriority.Low;
            _state.ExpiresAt = 0;
            _state.Owner = "";

            if (ModLogger.Enabled)
                ModLogger.Info($"EngineOverrideBus.Clear: Owner={owner}");
        }

        public static EngineIntent GetCurrent(out string owner, out EngineIntentPriority pri, out int expiresAt)
        {
            int now = Game.GameTime;
            if (now >= _state.ExpiresAt)
            {
                owner = "";
                pri = EngineIntentPriority.Low;
                expiresAt = 0;
                return EngineIntent.None;
            }

            owner = _state.Owner;
            pri = _state.Priority;
            expiresAt = _state.ExpiresAt;
            return _state.Intent;
        }
    }
}