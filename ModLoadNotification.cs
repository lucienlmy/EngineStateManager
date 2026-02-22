using System;
using System.IO;
using GTA;
using GTA.Native;

namespace EngineStateManager
{
    // Separate script: shows a native GTA notification on load (toggleable in INI).
    public sealed class ModLoadNotification : Script
    {
        private const string IniFileName = "EngineStateManager.ini";

        private readonly bool _enabled;
        private readonly bool _notifyOnLoad;
        private readonly string _loadText;

        private bool _didNotify;

        public ModLoadNotification()
        {
            try
            {
                var iniPath = Path.Combine(GetScriptsDir(), IniFileName);
                var ini = ScriptSettings.Load(iniPath);

                _enabled = ReadBoolLoose(ini, "Notifications", "Enabled", defaultValue: true);
                _notifyOnLoad = ReadBoolLoose(ini, "Notifications", "NotifyOnLoad", defaultValue: true);
                _loadText = ReadString(ini, "Notifications", "LoadText", "EngineStateManager loaded.");

                if (ModLogger.Enabled)
                    ModLogger.Info($"ModLoadNotification loaded. Enabled={_enabled}, NotifyOnLoad={_notifyOnLoad}, IniPath={iniPath}");

                Tick += OnTick;
                Interval = 0;
            }
            catch (Exception ex)
            {

                if (ModLogger.Enabled)
                    ModLogger.Error($"ModLoadNotification ctor failed: {ex}");
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_didNotify) return;
            _didNotify = true;

            if (!_enabled || !_notifyOnLoad) return;

            ShowNativeNotification(_loadText);
        }

        private static void ShowNativeNotification(string text)
        {
            try
            {

                const ulong SET_NOTIFICATION_TEXT_ENTRY = 0x202709F4C58A0424UL;
                const ulong DRAW_NOTIFICATION = 0x2ED7843F8F801023UL;

                string formatted =
                    "~h~~p~[ESM]~s~~p~ EngineStateManager~n~~s~" +
                    text;

                Function.Call((Hash)SET_NOTIFICATION_TEXT_ENTRY, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, formatted);
                Function.Call<int>((Hash)DRAW_NOTIFICATION, false, true);
            }
            catch
            {
                try
                {
                    GTA.UI.Notification.PostTicker("EngineStateManager: " + text, true);
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

        private static string ReadString(ScriptSettings ini, string section, string key, string defaultValue)
        {
            try
            {
                return ini.GetValue(section, key, defaultValue) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
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

            // Common boolean forms
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