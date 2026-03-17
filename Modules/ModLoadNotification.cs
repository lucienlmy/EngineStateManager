// Written by:
// 
// ███╗   ██╗ ██████╗  ██████╗██╗  ██╗ █████╗ ██╗      █████╗ 
// ████╗  ██║██╔═══██╗██╔════╝██║  ██║██╔══██╗██║     ██╔══██╗
// ██╔██╗ ██║██║   ██║██║     ███████║███████║██║     ███████║
// ██║╚██╗██║██║   ██║██║     ██╔══██║██╔══██║██║     ██╔══██║
// ██║ ╚████║╚██████╔╝╚██████╗██║  ██║██║  ██║███████╗██║  ██║
// ╚═╝  ╚═══╝ ╚═════╝  ╚═════╝╚═╝  ╚═╝╚═╝  ╚═╝╚══════╝╚═╝  ╚═╝
//
//          ░░▒▒▓▓ https://github.com/Nochala ▓▓▒▒░░

using GTA;
using GTA.Native;
using GTA.UI;
using System;

namespace EngineStateManager
{
    internal sealed class ModLoadNotification
    {
        private const string HardcodedText = "Initialized ~g~Successfully~s~";
        private const ulong SetNotificationTextEntry = 0x202709F4C58A0424UL;
        private const ulong DrawNotification = 0x2ED7843F8F801023UL;

        private bool _enabled = true;
        private bool _didNotify;

        public void Initialize()
        {
            try
            {
                _enabled = MainConfig.NotificationsEnabled;

                if (ModLogger.Enabled)
                {
                    ModLogger.Info($"ModLoadNotification loaded. Enabled={_enabled}, IniPath={MainConfig.IniPathUsed}");
                }
            }
            catch (Exception ex)
            {
                if (ModLogger.Enabled)
                {
                    ModLogger.Error($"ModLoadNotification init failed: {ex}");
                }
            }
        }

        public void OnTick()
        {
            if (_didNotify || !_enabled)
            {
                _didNotify = true;
                return;
            }

            _didNotify = true;
            ShowNativeNotification(HardcodedText);
        }

        private static void ShowNativeNotification(string text)
        {
            try
            {
                string formatted = "~h~~p~EngineStateManager~n~~s~" + text;

                Function.Call((Hash)SetNotificationTextEntry, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, formatted);
                Function.Call<int>((Hash)DrawNotification, false, true);
            }
            catch
            {
                try
                {
                    Notification.PostTicker("EngineStateManager: " + text, true);
                }
                catch
                {
                }
            }
        }
    }
}