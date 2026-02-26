
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

using GTA;
using GTA.Native;
using GTA.UI;
using System;
using System.IO;

namespace EngineStateManager
{
    internal sealed class ModLoadNotification
    {
        private const string IniFileName = "EngineStateManager.ini";
        private const string HardcodedText = "Initialized ~g~Successfully~s~";

        private bool _enabled = true;
        private bool _didNotify;

        public void Initialize()
        {
            try
            {
                // Centralized INI load (Main.cs)
                _enabled = MainConfig.NotificationsEnabled;

                if (ModLogger.Enabled)
                    ModLogger.Info($"ModLoadNotification loaded. Enabled={_enabled}, IniPath={MainConfig.IniPathUsed}");
            }
            catch (Exception ex)
            {
                if (ModLogger.Enabled)
                    ModLogger.Error($"ModLoadNotification init failed: {ex}");
            }
        }

        // Call once per frame from host Tick.
        public void OnTick()
        {
            if (_didNotify) return;
            _didNotify = true;

            if (!_enabled) return;

            ShowNativeNotification(HardcodedText);
        }

        private static void ShowNativeNotification(string text)
        {
            try
            {
                const ulong SET_NOTIFICATION_TEXT_ENTRY = 0x202709F4C58A0424UL;
                const ulong DRAW_NOTIFICATION = 0x2ED7843F8F801023UL;

                string formatted =
                    "~h~~p~EngineStateManager~n~~s~" +
                    text;

                Function.Call((Hash)SET_NOTIFICATION_TEXT_ENTRY, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, formatted);
                Function.Call<int>((Hash)DRAW_NOTIFICATION, false, true);
            }
            catch
            {
                try
                {
                    Notification.PostTicker("EngineStateManager: " + text, true);
                }
                catch { }
            }
        }

        private static string GetScriptsDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            baseDir = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string leaf = Path.GetFileName(baseDir);
            if (!string.IsNullOrEmpty(leaf) && leaf.Equals("scripts", StringComparison.OrdinalIgnoreCase))
                return baseDir;

            return Path.Combine(baseDir, "scripts");
        }

        private static bool ReadBoolLoose(ScriptSettings ini, string section, string key, bool defaultValue)
        {
            string raw = null;
            try { raw = ini.GetValue(section, key, defaultValue.ToString()); }
            catch { return defaultValue; }

            if (string.IsNullOrWhiteSpace(raw)) return defaultValue;

            raw = raw.Trim();
            int cut = raw.IndexOfAny(new[] { ';', '#', '(' });
            if (cut >= 0) raw = raw.Substring(0, cut).Trim();

            if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            if (raw.Equals("1", StringComparison.OrdinalIgnoreCase)) return true;
            if (raw.Equals("0", StringComparison.OrdinalIgnoreCase)) return false;
            if (raw.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (raw.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
            if (raw.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
            if (raw.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;

            return defaultValue;
        }
    }
}