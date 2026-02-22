using System;
using System.IO;
using GTA;

namespace EngineStateManager
{

    public sealed class MainPersist : Script
    {
        private const string IniFileName = "EngineStateManager.ini";

        // [Settings]
        private bool _disableAutoStart = true;
        private bool _disableAutoShutdown = true;

        // Optional tuning
        private int _entryEnforceMs = 900;
        private int _exitEnforceMs = 1400;

        // Entry state
        private bool _entryArmed;
        private int _entryVehicleHandle;
        private int _entryEnforceUntilGameTime;

        // Exit state (enforcement after we leave the seat)
        private bool _exitArmed;
        private int _exitVehicleHandle;
        private int _exitEnforceUntilGameTime;

        // Robust exit detection (works even if IS_PED_GETTING_OUT_OF_VEHICLE is flaky)
        private bool _wasInVehicle;
        private int _lastVehicleHandle;
        private bool _lastVehicleEngineOn;
        private bool _lastVehicleWasAircraft;
        private int _lastEngineOnGameTime;

        // Keep-engine-on flag management (prevents brief shutdown during exit animation)
        private int _keepEngineOnVehicleHandle;

        public MainPersist()
        {
            Interval = 50;

            LoadIniOnce();

            Tick += OnTick;
            Aborted += OnAborted;

            // Initialize transition tracking
            var p = Game.Player.Character;
            _wasInVehicle = p != null && p.Exists() && p.IsInVehicle();
            if (_wasInVehicle && p.CurrentVehicle != null && p.CurrentVehicle.Exists())
            {
                _lastVehicleHandle = p.CurrentVehicle.Handle;
                _lastVehicleEngineOn = NativeCompat.IsVehicleEngineOn(_lastVehicleHandle);
                if (_lastVehicleEngineOn) _lastEngineOnGameTime = Game.GameTime;
                _lastVehicleWasAircraft = IsAircraft(p.CurrentVehicle);
            }

            LogInfo($"MainPersist loaded. DisableAutoStart={_disableAutoStart}, DisableAutoShutdown={_disableAutoShutdown}, EntryEnforceMs={_entryEnforceMs}, ExitEnforceMs={_exitEnforceMs}");
        }

        private void OnAborted(object sender, EventArgs e)
        {
            try
            {
                if (_exitVehicleHandle != 0)
                    NativeCompat.SetVehicleKeepEngineOn(_exitVehicleHandle, false);
            }
            catch { }
        }

        private void OnTick(object sender, EventArgs e)
        {
            Ped player = Game.Player.Character;
            if (player == null || !player.Exists())
                return;

            bool inVehicle = player.IsInVehicle();
            Vehicle currentVeh = inVehicle ? player.CurrentVehicle : null;

            if (_disableAutoShutdown)
                MaintainKeepEngineOnFlag(player, currentVeh);
            else
                ClearKeepEngineOnFlagIfAny();

            if (inVehicle && currentVeh != null && currentVeh.Exists())
            {
                int prevHandle = _lastVehicleHandle;
                _lastVehicleHandle = currentVeh.Handle;
                if (_lastVehicleHandle != prevHandle) _lastEngineOnGameTime = 0;
                _lastVehicleWasAircraft = IsAircraft(currentVeh);

                _lastVehicleEngineOn = NativeCompat.IsVehicleEngineOn(_lastVehicleHandle);
                if (_lastVehicleEngineOn) _lastEngineOnGameTime = Game.GameTime;
                if (_lastVehicleEngineOn) _lastEngineOnGameTime = Game.GameTime;
            }

            if (_disableAutoStart)
                TryArmEntry(player);

            if (_disableAutoStart)
                ApplyEntryEnforcement();

            if (_disableAutoShutdown)
            {
                
                TryArmExitEarly(player, currentVeh);

                
                if (_wasInVehicle && !inVehicle)
                    ArmExitFromTransition();
            }

            if (_disableAutoShutdown)
                ApplyExitEnforcement();

            _wasInVehicle = inVehicle;
        }

        // =========================================================
        // DisableAutoStart (Entering) - Works for all vehicles incl aircraft
        // =========================================================
        private void TryArmEntry(Ped player)
        {
            if (_entryArmed)
                return;

            if (!NativeCompat.IsPedGettingIntoAnyVehicle(player.Handle))
                return;

            int targetVeh = NativeCompat.GetVehiclePedIsTryingToEnter(player.Handle);
            if (targetVeh == 0 || !NativeCompat.DoesEntityExist(targetVeh))
                return;

            // Only enforce if engine is OFF at entry start.
            if (NativeCompat.IsVehicleEngineOn(targetVeh))
                return;

            _entryArmed = true;
            _entryVehicleHandle = targetVeh;
            _entryEnforceUntilGameTime = Game.GameTime + _entryEnforceMs;

            LogInfoThrottled("mp_entry_arm", $"Entry armed. veh={targetVeh} engine was OFF -> enforcing OFF for {_entryEnforceMs}ms.", 500);
        }

        private void ApplyEntryEnforcement()
        {
            if (!_entryArmed)
                return;

            if (Game.GameTime > _entryEnforceUntilGameTime)
            {
                _entryArmed = false;
                _entryVehicleHandle = 0;
                return;
            }

            if (_entryVehicleHandle == 0 || !NativeCompat.DoesEntityExist(_entryVehicleHandle))
            {
                _entryArmed = false;
                _entryVehicleHandle = 0;
                return;
            }

            NativeCompat.ForceVehicleEngineOff_NoAutoStart(_entryVehicleHandle);
        }

        // =========================================================
        // DisableAutoShutdown (Exiting) - Skips aircraft exits
        // =========================================================

        private void TryArmExitEarly(Ped player, Vehicle currentVeh)
        {
            if (_exitArmed)
                return;

            if (!NativeCompat.IsPedGettingOutOfVehicle(player.Handle))
                return;

            if (currentVeh == null || !currentVeh.Exists())
                return;

            if (IsAircraft(currentVeh))
            {
                LogInfoThrottled("mp_exit_skip_aircraft", $"Exit detected for aircraft veh={currentVeh.Handle}; skipping DisableAutoShutdown enforcement.", 1500);
                return;
            }

            int vehHandle = currentVeh.Handle;

            // Snapshot from current state
            bool engineOn = NativeCompat.IsVehicleEngineOn(vehHandle);
            if (!engineOn)
                return;

            ArmExit(vehHandle, source: "native");
        }


        private void ArmExitFromTransition()
        {
            if (_exitArmed)
                return;

            if (_lastVehicleHandle == 0 || !NativeCompat.DoesEntityExist(_lastVehicleHandle))
                return;

            if (_lastVehicleWasAircraft)
            {
                LogInfoThrottled("mp_exit_skip_aircraft", $"Exit transition for aircraft veh={_lastVehicleHandle}; skipping DisableAutoShutdown enforcement.", 1500);
                return;
            }

            bool engineWasOnForExit = _lastVehicleEngineOn || (Game.GameTime - _lastEngineOnGameTime <= 1500);
            if (!engineWasOnForExit)
                return;

            ArmExit(_lastVehicleHandle, source: "transition");
        }

        private void ArmExit(int vehHandle, string source)
        {
            _exitArmed = true;
            _exitVehicleHandle = vehHandle;
            _exitEnforceUntilGameTime = Game.GameTime + _exitEnforceMs;

            // Start immediately
            NativeCompat.SetVehicleKeepEngineOn(_exitVehicleHandle, true);

            LogInfoThrottled("mp_exit_arm", $"Exit armed ({source}). veh={vehHandle} engine was ON -> enforcing ON for {_exitEnforceMs}ms.", 500);
        }

        private void ApplyExitEnforcement()
        {
            if (!_exitArmed)
                return;

            if (Game.GameTime > _exitEnforceUntilGameTime)
            {
                _exitArmed = false;
                _exitVehicleHandle = 0;
                return;
            }

            if (_exitVehicleHandle == 0 || !NativeCompat.DoesEntityExist(_exitVehicleHandle))
            {
                _exitArmed = false;
                _exitVehicleHandle = 0;
                return;
            }

            NativeCompat.SetVehicleKeepEngineOn(_exitVehicleHandle, true);

            if (!NativeCompat.IsVehicleEngineOn(_exitVehicleHandle))
                NativeCompat.ForceVehicleEngineOn(_exitVehicleHandle);
        }

        

        private void MaintainKeepEngineOnFlag(Ped player, Vehicle currentVeh)
        {
            if (currentVeh == null || !currentVeh.Exists())
            {
                ClearKeepEngineOnFlagIfAny();
                return;
            }

            // Driver only (passengers shouldn't force this)
            if (currentVeh.Driver != player)
            {
                ClearKeepEngineOnFlagIfAny();
                return;
            }

            // Skip aircraft by design
            if (IsAircraft(currentVeh))
            {
                ClearKeepEngineOnFlagIfAny();
                return;
            }

            int h = currentVeh.Handle;

            // If we changed vehicles, clear old flag first.
            if (_keepEngineOnVehicleHandle != 0 && _keepEngineOnVehicleHandle != h && NativeCompat.DoesEntityExist(_keepEngineOnVehicleHandle))
            {
                NativeCompat.SetVehicleKeepEngineOn(_keepEngineOnVehicleHandle, false);
            }

            _keepEngineOnVehicleHandle = h;

            // Keep applying every tick; this is cheap and prevents the brief engine dip.
            NativeCompat.SetVehicleKeepEngineOn(h, true);
        }

        private void ClearKeepEngineOnFlagIfAny()
        {
            if (_keepEngineOnVehicleHandle == 0)
                return;

            if (NativeCompat.DoesEntityExist(_keepEngineOnVehicleHandle))
                NativeCompat.SetVehicleKeepEngineOn(_keepEngineOnVehicleHandle, false);

            _keepEngineOnVehicleHandle = 0;
        }

//      // =========================================================
        // INI
        // =========================================================
        private void LoadIniOnce()
        {
            try
            {
                string scriptsDir = GetScriptsDir();
                string iniPath = Path.Combine(scriptsDir, IniFileName);

                LogInfo($"INI: LoadIniOnce path={iniPath} exists={File.Exists(iniPath)}");

                if (!File.Exists(iniPath))
                    return;

                var ini = ScriptSettings.Load(iniPath);

                string sec, raw;

                _disableAutoStart = ReadBoolFromAnySection(ini, "DisableAutoStart", true, out sec, out raw);
                LogInfo($"INI: DisableAutoStart section={sec} raw='{raw ?? "(missing)"}' -> {_disableAutoStart}");

                _disableAutoShutdown = ReadBoolFromAnySection(ini, "DisableAutoShutdown", true, out sec, out raw);
                LogInfo($"INI: DisableAutoShutdown section={sec} raw='{raw ?? "(missing)"}' -> {_disableAutoShutdown}");

                _entryEnforceMs = ReadIntFromAnySection(ini, "EntryEnforceMs", 900, 0, 5000, out sec, out raw);
                LogInfo($"INI: EntryEnforceMs section={sec} raw='{raw ?? "(missing)"}' -> {_entryEnforceMs}");

                _exitEnforceMs = ReadIntFromAnySection(ini, "ExitEnforceMs", 1400, 0, 7000, out sec, out raw);
                LogInfo($"INI: ExitEnforceMs section={sec} raw='{raw ?? "(missing)"}' -> {_exitEnforceMs}");
            }
            catch (Exception ex)
            {
                LogError("Failed to load EngineStateManager.ini (using defaults).", ex);
            }
        }

        private static string GetScriptsDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
            baseDir = Path.GetFullPath(baseDir);

            string trimmed = baseDir.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            string leaf = Path.GetFileName(trimmed);
            if (string.Equals(leaf, "scripts", StringComparison.OrdinalIgnoreCase))
                return trimmed;

            return Path.Combine(baseDir, "scripts");
        }


        private static readonly string[] IniSections = new[]
        {
            "Settings",
            "EngineStateManager",
            "MainPersist",
            "General",
        };

        private static bool ReadBoolFromAnySection(ScriptSettings ini, string key, bool defaultValue, out string foundSection, out string rawValue)
        {
            foundSection = "";
            rawValue = null;

            foreach (var section in IniSections)
            {
                // Use string so we can tolerate inline comments / extra text.
                string raw = ini.GetValue(section, key, (string)null);
                if (raw == null)
                    continue;

                rawValue = raw;
                foundSection = section;

                if (TryParseBoolLoose(raw, out bool b))
                    return b;

                // If key exists but parse failed, fall back to default.
                return defaultValue;
            }

            return defaultValue;
        }

        private static int ReadIntFromAnySection(ScriptSettings ini, string key, int defaultValue, int min, int max, out string foundSection, out string rawValue)
        {
            foundSection = "";
            rawValue = null;

            foreach (var section in IniSections)
            {
                string raw = ini.GetValue(section, key, (string)null);
                if (raw == null)
                    continue;

                rawValue = raw;
                foundSection = section;

                if (TryParseIntLoose(raw, out int v))
                    return Clamp(v, min, max);

                return Clamp(defaultValue, min, max);
            }

            return Clamp(defaultValue, min, max);
        }

        private static bool TryParseBoolLoose(string raw, out bool value)
        {
            value = false;
            if (raw == null) return false;

            string token = StripInlineJunk(raw);
            if (token.Length == 0) return false;

            token = token.ToLowerInvariant();

            if (token == "true" || token == "1" || token == "yes" || token == "y" || token == "on")
            {
                value = true;
                return true;
            }

            if (token == "false" || token == "0" || token == "no" || token == "n" || token == "off")
            {
                value = false;
                return true;
            }

            return false;
        }

        private static bool TryParseIntLoose(string raw, out int value)
        {
            value = 0;
            if (raw == null) return false;

            string token = StripInlineJunk(raw);
            if (token.Length == 0) return false;

            return int.TryParse(token, out value);
        }

        private static string StripInlineJunk(string raw)
        {
            string s = raw.Trim();

            // Common inline comment delimiters
            int cut = s.Length;

            int idx;
            idx = s.IndexOf(';'); if (idx >= 0 && idx < cut) cut = idx;
            idx = s.IndexOf('#'); if (idx >= 0 && idx < cut) cut = idx;
            idx = s.IndexOf('('); if (idx >= 0 && idx < cut) cut = idx;

            // Also handle "//"
            idx = s.IndexOf("//", StringComparison.Ordinal);
            if (idx >= 0 && idx < cut) cut = idx;

            s = s.Substring(0, cut).Trim();

            // If they wrote "key = value extra", keep first token.
            int sp = s.IndexOfAny(new[] { ' ', '\t' });
            if (sp > 0)
                s = s.Substring(0, sp).Trim();

            return s;
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static bool IsAircraft(Vehicle v)
        {
            try
            {
                if (v == null || !v.Exists())
                    return false;

                var m = v.Model;
                return m.IsPlane || m.IsHelicopter;
            }
            catch
            {
                return false;
            }
        }

        // =========================================================
        // Logging wrappers (NO control over Init/Enabled here)
        // =========================================================
        private static void LogInfo(string msg)
        {
            try
            {
                if (EngineStateManager.ModLogger.Enabled)
                    EngineStateManager.ModLogger.Info(msg);
            }
            catch { }
        }

        private static void LogInfoThrottled(string key, string msg, int ms)
        {
            try
            {
                if (EngineStateManager.ModLogger.Enabled)
                    EngineStateManager.ModLogger.InfoThrottled(key, msg, ms);
            }
            catch { }
        }

        private static void LogError(string msg, Exception ex = null)
        {
            try
            {
                if (!EngineStateManager.ModLogger.Enabled)
                    return;

                if (ex != null) EngineStateManager.ModLogger.Error(msg, ex);
                else EngineStateManager.ModLogger.Error(msg);
            }
            catch { }
        }
    }
}