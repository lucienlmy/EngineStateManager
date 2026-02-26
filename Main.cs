
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
using System;
using System.IO;
using System.Runtime;

namespace EngineStateManager
{

    public sealed class Main : Script
    {
        private readonly AircraftStallHandlers _aircraftStallHandlers = new AircraftStallHandlers();

        public Main()
        {
            MainConfig.EnsureLoaded();

            TryInitLogger();

            Tick += OnTick;
        }

        private void OnTick(object sender, EventArgs e)
        {
            _aircraftStallHandlers.Tick();
        }

        private static void TryInitLogger()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logFile = MainConfig.DebugLogFile;
                if (string.IsNullOrWhiteSpace(logFile))
                    logFile = "EngineStateManager.log";

                string preferredDir = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!preferredDir.EndsWith("scripts", StringComparison.OrdinalIgnoreCase))
                    preferredDir = Path.Combine(baseDir, "scripts");

                string preferred = Path.Combine(preferredDir, logFile);
                string fallback = preferred;

                ModLogger.Init(MainConfig.EnableDebugLogging, preferred, fallback);

                if (ModLogger.Enabled)
                    ModLogger.Info($"[Main] INI: {MainConfig.IniPathUsed}");
            }
            catch (Exception ex)
            {
                try { ModLogger.Error("[Main] Logger init failed.", ex); } catch { }
            }
        }
    }

    internal static class MainConfig
    {
        private const string IniFileName = "EngineStateManager.ini";

        private static readonly object _lock = new object();
        private static bool _loaded;

        private static string _iniAbsPath = "";
        private static ScriptSettings _ini;

        public static string IniPathUsed { get { EnsureLoaded(); return _iniAbsPath; } }
        public static bool IniExists { get { EnsureLoaded(); return _ini != null; } }

        // ===== AircraftEnginePersistence [Settings] =====
        public static bool DisableAircraftStallingWhenSlow { get { EnsureLoaded(); return _disableAircraftStallingWhenSlow; } }
        public static bool DisableDamagedAircraftStalling { get { EnsureLoaded(); return _disableDamagedAircraftStalling; } }
        public static int MaxTrackedAircraft { get { EnsureLoaded(); return _maxTrackedAircraft; } }
        public static int UpdateIntervalMs { get { EnsureLoaded(); return _updateIntervalMs; } }
        public static int ReassertPerAircraftMs { get { EnsureLoaded(); return _reassertPerAircraftMs; } }
        public static float MaxDistanceToTrack { get { EnsureLoaded(); return _maxDistanceToTrack; } }

        public static bool OnlyReassertWhenEngineOff { get { EnsureLoaded(); return _onlyReassertWhenEngineOff; } }
        public static bool ForceHeliBladesFullSpeedAfterExit { get { EnsureLoaded(); return _forceHeliBladesFullSpeedAfterExit; } }

        public static bool EnableHeliPersistence { get { EnsureLoaded(); return _enableHeliPersistence; } }
        public static bool EnablePlanePersistence { get { EnsureLoaded(); return _enablePlanePersistence; } }

        public static int JetExitGraceMs { get { EnsureLoaded(); return _jetExitGraceMs; } }
        public static int JetEnterGraceMs { get { EnsureLoaded(); return _jetEnterGraceMs; } }

        public static int PropExitGraceMs { get { EnsureLoaded(); return _propExitGraceMs; } }
        public static int PropEnterGraceMs { get { EnsureLoaded(); return _propEnterGraceMs; } }

        public static float JetIdleRpm { get { EnsureLoaded(); return _jetIdleRpm; } }

        public static bool EnableDebugLogging { get { EnsureLoaded(); return _enableDebugLogging; } }
        public static string DebugLogFile { get { EnsureLoaded(); return _debugLogFile; } }

        // ===== EngineStateControl [EngineToggleKeys] =====
        public static bool EngineToggleEnabled { get { EnsureLoaded(); return _engineToggleEnabled; } }
        public static bool EngineToggleAnimations { get { EnsureLoaded(); return _engineToggleAnimations; } }
        public static string EngineToggleKeyString { get { EnsureLoaded(); return _engineToggleKeyString; } }

        // ===== MainPersist (loose sections) =====
        public static bool DisableAutoStart { get { EnsureLoaded(); return _disableAutoStart; } }
        public static bool DisableAutoShutdown { get { EnsureLoaded(); return _disableAutoShutdown; } }
        public static int EntryEnforceMs { get { EnsureLoaded(); return _entryEnforceMs; } }
        public static int ExitEnforceMs { get { EnsureLoaded(); return _exitEnforceMs; } }
        public static int MaxTrackedVehicle { get { EnsureLoaded(); return _maxTrackedVehicle; } }

        // ===== Notifications =====
        public static bool NotificationsEnabled { get { EnsureLoaded(); return _notificationsEnabled; } }


        // Backing fields 
        private static int _maxTrackedAircraft = 20;
        private static int _updateIntervalMs = 250;
        private static int _reassertPerAircraftMs = 900;
        private static float _maxDistanceToTrack = -1f;

        private static bool _onlyReassertWhenEngineOff = true;
        private static bool _forceHeliBladesFullSpeedAfterExit = true;

        private static bool _enableHeliPersistence = true;
        private static bool _enablePlanePersistence = true;

        private static int _jetExitGraceMs = 800;
        private static int _jetEnterGraceMs = 800;

        private static int _propExitGraceMs = 2600;
        private static int _propEnterGraceMs = 1200;

        private static float _jetIdleRpm = 0.18f;

        private static bool _enableDebugLogging = false;
        private static string _debugLogFile = "EngineStateManager.log";

        private static bool _engineToggleEnabled = true;
        private static bool _engineToggleAnimations = true;
        private static string _engineToggleKeyString = "Z";

        private static bool _disableAutoStart = true;
        private static bool _disableAutoShutdown = true;
        private static int _entryEnforceMs = 900;
        private static int _exitEnforceMs = 1400;
        private static int _maxTrackedVehicle = 1;

        private static bool _notificationsEnabled = true;

        private static bool _disableAircraftStallingWhenSlow = false;
        private static bool _disableDamagedAircraftStalling = false;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;

                _iniAbsPath = FindIniPath();

                try
                {
                    if (File.Exists(_iniAbsPath))
                        _ini = ScriptSettings.Load(_iniAbsPath);
                    else
                        _ini = null;
                }
                catch
                {
                    _ini = null;
                }

                LoadAllValues();
                _loaded = true;
            }
        }

        private static void LoadAllValues()
        {

            if (_ini == null)
                return;

            // AircraftEnginePersistence reads [Settings]
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
            if (_jetIdleRpm < 0f) _jetIdleRpm = 0f;
            if (_jetIdleRpm > 1f) _jetIdleRpm = 1f;

            // Logging reads[Debug]
            _enableDebugLogging = ReadBool(_ini, "Debug", "EnableDebugLogging", _enableDebugLogging);
            _debugLogFile = ReadString(_ini, "Debug", "DebugLogFile", _debugLogFile);

            // EngineStateControl reads [EngineToggleKeys]
            _engineToggleEnabled = ReadBool(_ini, "EngineToggleKeys", "ENABLED", _engineToggleEnabled);
            _engineToggleAnimations = ReadBool(_ini, "EngineToggleKeys", "ANIMATIONS", _engineToggleAnimations);
            _engineToggleKeyString = ReadString(_ini, "EngineToggleKeys", "MAIN", _engineToggleKeyString);

            // ModLoadNotification reads [Notifications]
            _notificationsEnabled = ReadBoolLoose(_ini, "Notifications", "Enabled", _notificationsEnabled);

            // MainPersist [Settings]
            _disableAutoStart = ReadBool(_ini, "Settings", "DisableAutoStart", _disableAutoStart);
            _disableAutoShutdown = ReadBool(_ini, "Settings", "DisableAutoShutdown", _disableAutoShutdown);
            _entryEnforceMs = Clamp(ReadInt(_ini, "Settings", "EntryEnforceMs", _entryEnforceMs), 0, 5000);
            _exitEnforceMs = Clamp(ReadInt(_ini, "Settings", "ExitEnforceMs", _exitEnforceMs), 0, 7000);
            _maxTrackedVehicle = Clamp(ReadInt(_ini, "Settings", "MaxTrackedVehicle", _maxTrackedVehicle), 0, 200);

            // AircraftStallHandlers reads [Settings] STRONG
            _disableAircraftStallingWhenSlow = ReadBool(_ini, "Settings", "DisableAircraftStallingWhenSlow", _disableAircraftStallingWhenSlow);
            _disableDamagedAircraftStalling = ReadBool(_ini, "Settings", "DisableDamagedAircraftStalling", _disableDamagedAircraftStalling);
        }

        private static string FindIniPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string p1 = Path.Combine(baseDir, "scripts", IniFileName);
            if (File.Exists(p1)) return p1;

            string p2 = Path.Combine("scripts", IniFileName);
            if (File.Exists(p2)) return p2;

            string p3 = Path.Combine(baseDir, IniFileName);
            if (File.Exists(p3)) return p3;

            return p1; // default 
        }

        private static bool ReadBool(ScriptSettings ini, string section, string key, bool def)
        {
            try { return ini.GetValue(section, key, def); } catch { return def; }
        }

        private static int ReadInt(ScriptSettings ini, string section, string key, int def)
        {
            try { return ini.GetValue(section, key, def); } catch { return def; }
        }

        private static float ReadFloat(ScriptSettings ini, string section, string key, float def)
        {
            try { return ini.GetValue(section, key, def); } catch { return def; }
        }

        private static string ReadString(ScriptSettings ini, string section, string key, string def)
        {
            try { return ini.GetValue(section, key, def); } catch { return def; }
        }

        private static bool ReadBoolLoose(ScriptSettings ini, string section, string key, bool def)
        {
            try { return ini.GetValue(section, key, def); } catch { return def; }
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}