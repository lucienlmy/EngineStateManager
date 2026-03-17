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
using System;
using System.IO;
using System.Windows.Forms;
using Control = GTA.Control;

namespace EngineStateManager
{
    public sealed class Main : Script
    {
        private readonly AircraftStallHandlers _aircraftStallHandlers = new AircraftStallHandlers();
        private int _nextRuntimeRefreshGameTime;
        private bool _lastLoggerEnabled;
        private string _lastLoggerFile = "EngineStateManager.log";

        public Main()
        {
            MainConfig.EnsureLoaded();

            TryInitLogger();
            _lastLoggerEnabled = MainConfig.EnableDebugLogging;
            _lastLoggerFile = MainConfig.DebugLogFile ?? "EngineStateManager.log";

            Tick += OnTick;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (Game.GameTime >= _nextRuntimeRefreshGameTime)
            {
                RefreshRuntimeSystems();
                _nextRuntimeRefreshGameTime = Game.GameTime + 500;
            }

            _aircraftStallHandlers.Tick();
        }

        private void RefreshRuntimeSystems()
        {
            try
            {
                bool changed = MainConfig.RefreshIfChanged();
                if (!changed)
                    return;

                string loggerFile = MainConfig.DebugLogFile ?? "EngineStateManager.log";
                bool loggerEnabled = MainConfig.EnableDebugLogging;

                if (loggerEnabled != _lastLoggerEnabled ||
                    !string.Equals(loggerFile, _lastLoggerFile, StringComparison.OrdinalIgnoreCase))
                {
                    TryInitLogger();
                    _lastLoggerEnabled = loggerEnabled;
                    _lastLoggerFile = loggerFile;
                }
                else if (ModLogger.Enabled)
                {
                    ModLogger.Info($"[Main] INI reloaded: {MainConfig.IniPathUsed}");
                }
            }
            catch (Exception ex)
            {
                try
                {
                    ModLogger.Error("[Main] Runtime refresh failed.", ex);
                }
                catch
                {
                }
            }
        }

        private static void TryInitLogger()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logFile = MainConfig.DebugLogFile;

                if (string.IsNullOrWhiteSpace(logFile))
                    logFile = "EngineStateManager.log";

                string normalizedBaseDir = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string preferredDir = normalizedBaseDir;

                if (!preferredDir.EndsWith("scripts", StringComparison.OrdinalIgnoreCase))
                    preferredDir = Path.Combine(normalizedBaseDir, "scripts");

                string preferred = Path.Combine(preferredDir, logFile);
                string fallback = Path.Combine(normalizedBaseDir, logFile);

                ModLogger.Init(MainConfig.EnableDebugLogging, preferred, fallback);

                if (ModLogger.Enabled)
                    ModLogger.Info($"[Main] INI: {MainConfig.IniPathUsed}");
            }
            catch (Exception ex)
            {
                try
                {
                    ModLogger.Error("[Main] Logger init failed.", ex);
                }
                catch
                {
                }
            }
        }
    }

    internal static class MainConfig
    {
        private const string IniFileName = "EngineStateManager.ini";

        private static readonly object _lock = new object();
        private static bool _loaded;
        private static string _iniAbsPath = string.Empty;
        private static ScriptSettings _ini;
        private static DateTime _iniLastWriteUtc = DateTime.MinValue;

        private static int _maxTrackedAircraft = 20;
        private static int _updateIntervalMs = 400;
        private static int _reassertPerAircraftMs = 1000;
        private static float _maxDistanceToTrack = 2000f;
        private static bool _onlyReassertWhenEngineOff = true;
        private static bool _forceHeliBladesFullSpeedAfterExit = true;
        private static bool _enableHeliPersistence = true;
        private static bool _enablePlanePersistence = true;
        private static int _jetExitGraceMs = 800;
        private static int _jetEnterGraceMs = 800;
        private static int _propExitGraceMs = 2600;
        private static int _propEnterGraceMs = 1200;
        private static float _jetIdleRpm = 0.15f;
        private static bool _enableDebugLogging;
        private static string _debugLogFile = "EngineStateManager.log";
        private static bool _engineToggleEnabled = true;
        private static string _engineToggleKeyString = "Z";
        private static int _engineToggleMainVk = 0x5A;
        private static bool _engineToggleControllerEnabled;
        private static string _engineToggleControllerString = "VehicleDuck";
        private static Control _engineToggleControllerMain = Control.VehicleDuck;
        private static bool _disableAutoStart = true;
        private static bool _disableAutoShutdown = true;
        private static int _entryEnforceMs = 900;
        private static int _exitEnforceMs = 1400;
        private static int _maxTrackedVehicle = 1;
        private static bool _notificationsEnabled = true;
        private static bool _disableAircraftStallingWhenSlow;
        private static bool _disableDamagedAircraftStalling;

        public static string IniPathUsed
        {
            get
            {
                EnsureLoaded();
                return _iniAbsPath;
            }
        }

        public static bool IniExists
        {
            get
            {
                EnsureLoaded();
                return _ini != null;
            }
        }

        public static bool DisableAircraftStallingWhenSlow
        {
            get
            {
                EnsureLoaded();
                return _disableAircraftStallingWhenSlow;
            }
        }

        public static bool DisableDamagedAircraftStalling
        {
            get
            {
                EnsureLoaded();
                return _disableDamagedAircraftStalling;
            }
        }

        public static int MaxTrackedAircraft
        {
            get
            {
                EnsureLoaded();
                return _maxTrackedAircraft;
            }
        }

        public static int UpdateIntervalMs
        {
            get
            {
                EnsureLoaded();
                return _updateIntervalMs;
            }
        }

        public static int ReassertPerAircraftMs
        {
            get
            {
                EnsureLoaded();
                return _reassertPerAircraftMs;
            }
        }

        public static float MaxDistanceToTrack
        {
            get
            {
                EnsureLoaded();
                return _maxDistanceToTrack;
            }
        }

        public static bool OnlyReassertWhenEngineOff
        {
            get
            {
                EnsureLoaded();
                return _onlyReassertWhenEngineOff;
            }
        }

        public static bool ForceHeliBladesFullSpeedAfterExit
        {
            get
            {
                EnsureLoaded();
                return _forceHeliBladesFullSpeedAfterExit;
            }
        }

        public static bool EnableHeliPersistence
        {
            get
            {
                EnsureLoaded();
                return _enableHeliPersistence;
            }
        }

        public static bool EnablePlanePersistence
        {
            get
            {
                EnsureLoaded();
                return _enablePlanePersistence;
            }
        }

        public static int JetExitGraceMs
        {
            get
            {
                EnsureLoaded();
                return _jetExitGraceMs;
            }
        }

        public static int JetEnterGraceMs
        {
            get
            {
                EnsureLoaded();
                return _jetEnterGraceMs;
            }
        }

        public static int PropExitGraceMs
        {
            get
            {
                EnsureLoaded();
                return _propExitGraceMs;
            }
        }

        public static int PropEnterGraceMs
        {
            get
            {
                EnsureLoaded();
                return _propEnterGraceMs;
            }
        }

        public static float JetIdleRpm
        {
            get
            {
                EnsureLoaded();
                return _jetIdleRpm;
            }
        }

        public static bool EnableDebugLogging
        {
            get
            {
                EnsureLoaded();
                return _enableDebugLogging;
            }
        }

        public static string DebugLogFile
        {
            get
            {
                EnsureLoaded();
                return _debugLogFile;
            }
        }

        public static bool EngineToggleEnabled
        {
            get
            {
                EnsureLoaded();
                return _engineToggleEnabled;
            }
        }

        public static string EngineToggleKeyString
        {
            get
            {
                EnsureLoaded();
                return _engineToggleKeyString;
            }
        }

        public static int EngineToggleMainVk
        {
            get
            {
                EnsureLoaded();
                return _engineToggleMainVk;
            }
        }

        public static bool EngineToggleControllerEnabled
        {
            get
            {
                EnsureLoaded();
                return _engineToggleControllerEnabled;
            }
        }

        public static Control EngineToggleControllerMain
        {
            get
            {
                EnsureLoaded();
                return _engineToggleControllerMain;
            }
        }

        public static string EngineToggleControllerString
        {
            get
            {
                EnsureLoaded();
                return _engineToggleControllerString;
            }
        }

        public static bool DisableAutoStart
        {
            get
            {
                EnsureLoaded();
                return _disableAutoStart;
            }
        }

        public static bool DisableAutoShutdown
        {
            get
            {
                EnsureLoaded();
                return _disableAutoShutdown;
            }
        }

        public static int EntryEnforceMs
        {
            get
            {
                EnsureLoaded();
                return _entryEnforceMs;
            }
        }

        public static int ExitEnforceMs
        {
            get
            {
                EnsureLoaded();
                return _exitEnforceMs;
            }
        }

        public static int MaxTrackedVehicle
        {
            get
            {
                EnsureLoaded();
                return _maxTrackedVehicle;
            }
        }

        public static bool NotificationsEnabled
        {
            get
            {
                EnsureLoaded();
                return _notificationsEnabled;
            }
        }

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            lock (_lock)
            {
                if (_loaded)
                    return;

                ReloadFromDisk();
                _loaded = true;
            }
        }

        public static bool RefreshIfChanged()
        {
            lock (_lock)
            {
                if (!_loaded)
                {
                    ReloadFromDisk();
                    _loaded = true;
                    return true;
                }

                string newPath = FindIniPath();
                DateTime newWriteUtc = File.Exists(newPath)
                    ? File.GetLastWriteTimeUtc(newPath)
                    : DateTime.MinValue;

                bool pathChanged = !string.Equals(_iniAbsPath, newPath, StringComparison.OrdinalIgnoreCase);
                bool writeChanged = newWriteUtc != _iniLastWriteUtc;

                if (!pathChanged && !writeChanged)
                    return false;

                ReloadFromDisk();
                return true;
            }
        }

        private static void ReloadFromDisk()
        {
            _iniAbsPath = FindIniPath();
            _iniLastWriteUtc = File.Exists(_iniAbsPath)
                ? File.GetLastWriteTimeUtc(_iniAbsPath)
                : DateTime.MinValue;

            try
            {
                _ini = File.Exists(_iniAbsPath) ? ScriptSettings.Load(_iniAbsPath) : null;
            }
            catch
            {
                _ini = null;
            }

            ResetDefaults();
            LoadAllValues();
        }

        private static void ResetDefaults()
        {
            _maxTrackedAircraft = 20;
            _updateIntervalMs = 400;
            _reassertPerAircraftMs = 1000;
            _maxDistanceToTrack = 2000f;
            _onlyReassertWhenEngineOff = true;
            _forceHeliBladesFullSpeedAfterExit = true;
            _enableHeliPersistence = true;
            _enablePlanePersistence = true;
            _jetExitGraceMs = 800;
            _jetEnterGraceMs = 800;
            _propExitGraceMs = 2600;
            _propEnterGraceMs = 1200;
            _jetIdleRpm = 0.15f;
            _enableDebugLogging = false;
            _debugLogFile = "EngineStateManager.log";
            _engineToggleEnabled = true;
            _engineToggleKeyString = "Z";
            _engineToggleMainVk = 0x5A;
            _engineToggleControllerEnabled = false;
            _engineToggleControllerString = "VehicleDuck";
            _engineToggleControllerMain = Control.VehicleDuck;
            _disableAutoStart = true;
            _disableAutoShutdown = true;
            _entryEnforceMs = 900;
            _exitEnforceMs = 1400;
            _maxTrackedVehicle = 1;
            _notificationsEnabled = true;
            _disableAircraftStallingWhenSlow = false;
            _disableDamagedAircraftStalling = false;
        }

        private static void LoadAllValues()
        {
            if (_ini == null)
                return;

            _maxTrackedAircraft = Clamp(ReadInt(_ini, "Settings", "MaxTrackedAircraft", _maxTrackedAircraft), 1, 200);
            _updateIntervalMs = Clamp(ReadInt(_ini, "Settings", "UpdateIntervalMs", _updateIntervalMs), 50, 5000);
            _reassertPerAircraftMs = Clamp(ReadInt(_ini, "Settings", "ReassertPerAircraftMs", _reassertPerAircraftMs), 50, 10000);
            _maxDistanceToTrack = ReadFloat(_ini, "Settings", "MaxDistanceToTrack", _maxDistanceToTrack);
            _onlyReassertWhenEngineOff = ReadBool(_ini, "Settings", "OnlyReassertWhenEngineOff", _onlyReassertWhenEngineOff);
            _forceHeliBladesFullSpeedAfterExit = ReadBool(_ini, "Settings", "ForceHeliBladesFullSpeedAfterExit", _forceHeliBladesFullSpeedAfterExit);
            _enableHeliPersistence = ReadBool(_ini, "Settings", "EnableHeliPersistence", _enableHeliPersistence);
            _enablePlanePersistence = ReadBool(_ini, "Settings", "EnablePlanePersistence", _enablePlanePersistence);
            _jetExitGraceMs = Clamp(ReadInt(_ini, "Settings", "JetExitGraceMs", _jetExitGraceMs), 0, 5000);
            _jetEnterGraceMs = Clamp(ReadInt(_ini, "Settings", "JetEnterGraceMs", _jetEnterGraceMs), 0, 5000);
            _propExitGraceMs = Clamp(ReadInt(_ini, "Settings", "PropExitGraceMs", _propExitGraceMs), 0, 8000);
            _propEnterGraceMs = Clamp(ReadInt(_ini, "Settings", "PropEnterGraceMs", _propEnterGraceMs), 0, 8000);
            _jetIdleRpm = ReadFloat(_ini, "Settings", "JetIdleRpm", _jetIdleRpm);

            if (_jetIdleRpm < 0f)
                _jetIdleRpm = 0f;
            else if (_jetIdleRpm > 1f)
                _jetIdleRpm = 1f;

            _enableDebugLogging = ReadBool(_ini, "Debug", "EnableDebugLogging", _enableDebugLogging);
            _debugLogFile = ReadString(_ini, "Debug", "DebugLogFile", _debugLogFile);
            _engineToggleEnabled = ReadBool(_ini, "EngineToggleKeys", "ENABLED", _engineToggleEnabled);
            _engineToggleKeyString = ReadString(_ini, "EngineToggleKeys", "MAIN", _engineToggleKeyString);
            _engineToggleMainVk = ParseVirtualKey(_engineToggleKeyString, _engineToggleMainVk);
            _engineToggleControllerEnabled = ReadBool(_ini, "EngineToggleKeys", "CONTROLLER_ENABLED", _engineToggleControllerEnabled);
            _engineToggleControllerString = ReadString(_ini, "EngineToggleKeys", "CONTROLLER_MAIN", _engineToggleControllerString);
            _engineToggleControllerMain = ParseControl(_engineToggleControllerString, _engineToggleControllerMain);
            _notificationsEnabled = ReadBoolLoose(_ini, "Notifications", "Enabled", _notificationsEnabled);
            _disableAutoStart = ReadBool(_ini, "Settings", "DisableAutoStart", _disableAutoStart);
            _disableAutoShutdown = ReadBool(_ini, "Settings", "DisableAutoShutdown", _disableAutoShutdown);
            _entryEnforceMs = Clamp(ReadInt(_ini, "Settings", "EntryEnforceMs", _entryEnforceMs), 0, 5000);
            _exitEnforceMs = Clamp(ReadInt(_ini, "Settings", "ExitEnforceMs", _exitEnforceMs), 0, 7000);
            _maxTrackedVehicle = Clamp(ReadInt(_ini, "Settings", "MaxTrackedVehicle", _maxTrackedVehicle), 0, 200);
            _disableAircraftStallingWhenSlow = ReadBool(_ini, "Settings", "DisableAircraftStallingWhenSlow", _disableAircraftStallingWhenSlow);
            _disableDamagedAircraftStalling = ReadBool(_ini, "Settings", "DisableDamagedAircraftStalling", _disableDamagedAircraftStalling);
        }

        private static string FindIniPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string p1 = Path.Combine(baseDir, "scripts", IniFileName);
            if (File.Exists(p1))
                return p1;

            string p2 = Path.Combine("scripts", IniFileName);
            if (File.Exists(p2))
                return p2;

            string p3 = Path.Combine(baseDir, IniFileName);
            if (File.Exists(p3))
                return p3;

            return p1;
        }

        private static bool ReadBool(ScriptSettings ini, string section, string key, bool def)
        {
            try
            {
                return ini.GetValue(section, key, def);
            }
            catch
            {
                return def;
            }
        }

        private static int ReadInt(ScriptSettings ini, string section, string key, int def)
        {
            try
            {
                return ini.GetValue(section, key, def);
            }
            catch
            {
                return def;
            }
        }

        private static float ReadFloat(ScriptSettings ini, string section, string key, float def)
        {
            try
            {
                return ini.GetValue(section, key, def);
            }
            catch
            {
                return def;
            }
        }

        private static string ReadString(ScriptSettings ini, string section, string key, string def)
        {
            try
            {
                return ini.GetValue(section, key, def);
            }
            catch
            {
                return def;
            }
        }

        private static bool ReadBoolLoose(ScriptSettings ini, string section, string key, bool def)
        {
            try
            {
                return ini.GetValue(section, key, def);
            }
            catch
            {
                return def;
            }
        }

        private static int ParseVirtualKey(string s, int fallbackVk)
        {
            if (string.IsNullOrWhiteSpace(s))
                return fallbackVk;

            s = s.Trim();

            if (s.StartsWith("VK_", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(3);

            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                int hex;
                if (int.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out hex))
                    return hex & 0xFF;

                return fallbackVk;
            }

            int dec;
            if (int.TryParse(s, out dec))
                return dec & 0xFF;

            if (s.Length == 1)
            {
                char c = char.ToUpperInvariant(s[0]);
                if (c >= 'A' && c <= 'Z')
                    return c;
                if (c >= '0' && c <= '9')
                    return c;
            }

            Keys key;
            if (Enum.TryParse(s, true, out key))
                return ((int)key) & 0xFF;

            return fallbackVk;
        }

        private static Control ParseControl(string s, Control fallback)
        {
            if (string.IsNullOrWhiteSpace(s))
                return fallback;

            s = s.Trim();

            int num;
            if (int.TryParse(s, out num))
            {
                try
                {
                    return (Control)num;
                }
                catch
                {
                    return fallback;
                }
            }

            Control parsed;
            if (Enum.TryParse(s, true, out parsed))
                return parsed;

            return fallback;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
