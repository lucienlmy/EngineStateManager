
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

using System;
using System.IO;
using System.Collections.Generic;
using GTA;
using GTA.Native;

namespace EngineStateManager
{
    public sealed class MainPersist : Script
    {
        private bool _disableAutoStart = true;
        private bool _disableAutoShutdown = true;

        private int _entryEnforceMs = 900;
        private int _exitEnforceMs = 1400;

        private float _maxDistanceToTrackFeet = -1f;
        private const float FeetPerMeter = 3.28084f;

        private int _maxTrackedVehicle = 1;

        private readonly LinkedList<int> _persistedLru = new LinkedList<int>();
        private readonly HashSet<int> _persistedSet = new HashSet<int>();

        private bool _entryArmed;
        private int _entryVehicleHandle;
        private int _entryEnforceUntilGameTime;

        private bool _exitArmed;
        private int _exitVehicleHandle;
        private int _exitEnforceUntilGameTime;

        private bool _wasInVehicle;
        private int _lastVehicleHandle;
        private bool _lastVehicleEngineOn;
        private bool _lastVehicleWasAircraft;
        private int _lastEngineOnGameTime;

        private int _keepEngineOnVehicleHandle;

        private int _recentDriverVehicleHandle;
        private int _recentDriverGameTime;
        private bool _recentDriverEngineWasOn;
        private const int RecentDriverGraceMs = 2500;

        private bool _seatShuffleExitPending;
        private int _seatShuffleVehicleHandle;
        private int _seatShuffleExitUntilGameTime;
        private const int SeatShuffleExitEnforceMs = 14000;
        private const int PostExitBrakeClearMs = 7000;
        private const int HeavyVehiclePostExitBrakeClearMs = 9000;
        private const int HeavyVehicleShuffleExitEnforceMs = 18000;

        private int _postExitBrakeVehicleHandle;
        private int _postExitBrakeUntilGameTime;
        private bool _postExitBrakeKeepEngineOn;

        public MainPersist()
        {
            Interval = 0;

            LoadIniOnce();

            Tick += OnTick;
            Aborted += OnAborted;

            var p = Game.Player.Character;
            _wasInVehicle = p != null && p.Exists() && p.IsInVehicle();
            if (_wasInVehicle && p.CurrentVehicle != null && p.CurrentVehicle.Exists())
            {
                _lastVehicleHandle = p.CurrentVehicle.Handle;
                _lastVehicleEngineOn = NativeCompat.IsVehicleEngineOn(_lastVehicleHandle);
                if (_lastVehicleEngineOn)
                    _lastEngineOnGameTime = Game.GameTime;
                _lastVehicleWasAircraft = IsAircraft(p.CurrentVehicle);
            }

            LogInfo($"MainPersist loaded. DisableAutoStart={_disableAutoStart}, DisableAutoShutdown={_disableAutoShutdown}, EntryEnforceMs={_entryEnforceMs}, ExitEnforceMs={_exitEnforceMs}");
        }

        private void OnAborted(object sender, EventArgs e)
        {
            try
            {
                if (_exitVehicleHandle != 0)
                {
                    NativeCompat.SetVehicleKeepEngineOn(_exitVehicleHandle, false);
                    NativeCompat.SetVehicleKeepEngineOnWhenAbandoned(_exitVehicleHandle, false);
                }
            }
            catch { }
        }

        private void OnTick(object sender, EventArgs e)
        {
            Ped player = Game.Player.Character;
            if (player == null || !player.Exists())
                return;

            if (Interval != 0)
                Interval = 0;

            bool inVehicle = player.IsInVehicle();
            Vehicle currentVeh = inVehicle ? player.CurrentVehicle : null;

            bool carPersistEnabled = IsCarPersistenceEnabled();
            if (carPersistEnabled)
                MaintainPersistedVehicles(player);
            else
                ClearAllPersistedVehicles();

            if (_disableAutoShutdown)
                MaintainKeepEngineOnFlag(player, currentVeh);
            else
                ClearKeepEngineOnFlagIfAny();

            if (_disableAutoShutdown)
                UpdateSeatShuffleExitGuard(player, currentVeh);
            else
                ClearSeatShuffleExitGuard();

            UpdatePostExitBrakeClear(player);

            if (inVehicle && currentVeh != null && currentVeh.Exists())
            {
                if (currentVeh.Driver == player)
                {
                    _recentDriverVehicleHandle = currentVeh.Handle;
                    _recentDriverGameTime = Game.GameTime;
                    _recentDriverEngineWasOn = NativeCompat.IsVehicleEngineOn(currentVeh.Handle);
                }

                if (!BusForcesOff() &&
                    currentVeh.Driver != player &&
                    currentVeh.Handle == _recentDriverVehicleHandle &&
                    (Game.GameTime - _recentDriverGameTime) <= RecentDriverGraceMs &&
                    _recentDriverEngineWasOn &&
                    !IsAircraft(currentVeh))
                {
                    int enforceMs = Math.Max(_exitEnforceMs, 4500);
                    ArmExit(currentVeh.Handle, "shuffle", enforceMs);
                }

                int prevHandle = _lastVehicleHandle;
                _lastVehicleHandle = currentVeh.Handle;
                if (_lastVehicleHandle != prevHandle)
                    _lastEngineOnGameTime = 0;
                _lastVehicleWasAircraft = IsAircraft(currentVeh);

                _lastVehicleEngineOn = NativeCompat.IsVehicleEngineOn(_lastVehicleHandle);
                if (_lastVehicleEngineOn)
                    _lastEngineOnGameTime = Game.GameTime;
            }

            if (_disableAutoStart)
                TryArmEntry(player);

            if (_disableAutoStart)
                ApplyEntryEnforcement();

            if (_disableAutoShutdown)
            {
                TryArmExitIntent(player, currentVeh);
                TryArmExitEarly(player, currentVeh);

                if (_wasInVehicle && !inVehicle)
                    ArmExitFromTransition();
            }

            if (_disableAutoShutdown)
                ApplyExitEnforcement();

            _wasInVehicle = inVehicle;
        }

        private void TryArmEntry(Ped player)
        {
            if (_entryArmed)
                return;

            if (!NativeCompat.IsPedGettingIntoAnyVehicle(player.Handle))
                return;

            int targetVeh = NativeCompat.GetVehiclePedIsTryingToEnter(player.Handle);
            if (targetVeh == 0 || !NativeCompat.DoesEntityExist(targetVeh))
                return;

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

            if (NativeCompat.IsVehicleEngineOn(_entryVehicleHandle))
            {
                ModLogger.Info("[EntryGuard] Vehicle engine already running - canceling DisableAutoStart enforcement.");
                _entryArmed = false;
                _entryVehicleHandle = 0;
                return;
            }

            NativeCompat.ForceVehicleEngineOff_NoAutoStart(_entryVehicleHandle);
        }

        private void TryArmExitIntent(Ped player, Vehicle currentVeh)
        {
            if (_exitArmed)
                return;

            if (currentVeh == null || !currentVeh.Exists())
                return;

            if (currentVeh.Driver != player)
                return;

            if (IsAircraft(currentVeh))
                return;

            if (BusForcesOff())
                return;

            bool exitPressed =
                Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)Control.VehicleExit) ||
                Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 2, (int)Control.VehicleExit) ||
                Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)Control.VehicleExit) ||
                Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 2, (int)Control.VehicleExit);

            bool exitHeld =
                Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.VehicleExit) ||
                Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 2, (int)Control.VehicleExit) ||
                Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.VehicleExit) ||
                Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 2, (int)Control.VehicleExit);

            if (!(exitPressed || (exitHeld && NativeCompat.IsPedGettingOutOfVehicle(player.Handle))))
                return;

            int vehHandle = currentVeh.Handle;
            bool engineOn = NativeCompat.IsVehicleEngineOn(vehHandle);
            if (!engineOn)
                return;

            int enforceMs = GetSeatShuffleExitEnforceMs(vehHandle);
            ArmExit(vehHandle, "exit_intent", enforceMs);
            ProtectRunningVehicleNow(vehHandle);
            BeginPostExitBrakeClear(vehHandle, true);
        }

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
            bool engineOn = NativeCompat.IsVehicleEngineOn(vehHandle);
            if (!engineOn)
                return;

            ArmExit(vehHandle, "native");
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

            ArmExit(_lastVehicleHandle, "transition");
        }

        private void ArmExit(int vehHandle, string source, int enforceMsOverride = -1)
        {
            _exitArmed = true;
            _exitVehicleHandle = vehHandle;
            _exitEnforceUntilGameTime = Game.GameTime + ((enforceMsOverride > 0) ? enforceMsOverride : _exitEnforceMs);

            TouchPersistedVehicle(vehHandle);

            EngineIntent busIntent = EngineOverrideBus.GetCurrent(out _, out _, out _);
            if (busIntent == EngineIntent.ForceOff)
            {
                NativeCompat.SetVehicleKeepEngineOn(_exitVehicleHandle, false);
                _exitArmed = false;
                _exitVehicleHandle = 0;
                return;
            }

            NativeCompat.SetVehicleKeepEngineOn(_exitVehicleHandle, true);
            NativeCompat.SetVehicleKeepEngineOnWhenAbandoned(_exitVehicleHandle, true);

            LogInfoThrottled("mp_exit_arm", $"Exit armed ({source}). veh={vehHandle} engine was ON -> enforcing ON for {_exitEnforceMs}ms.", 500);
        }

        private void UpdateSeatShuffleExitGuard(Ped player, Vehicle currentVeh)
        {
            if (player == null || !player.Exists())
            {
                ClearSeatShuffleExitGuard();
                return;
            }

            if (BusForcesOff())
            {
                ClearSeatShuffleExitGuard();
                return;
            }

            bool playerInVehicle = player.IsInVehicle();

            if (playerInVehicle && currentVeh != null && currentVeh.Exists() && !IsAircraft(currentVeh))
            {
                bool sameAsRecentDriverVehicle =
                    _recentDriverVehicleHandle != 0 &&
                    currentVeh.Handle == _recentDriverVehicleHandle &&
                    (Game.GameTime - _recentDriverGameTime) <= RecentDriverGraceMs;

                bool isPassengerOrShuffling = currentVeh.Driver != player;

                if (sameAsRecentDriverVehicle && isPassengerOrShuffling && _recentDriverEngineWasOn)
                    BeginOrRefreshSeatShuffleExitGuard(currentVeh);
            }

            if (!_seatShuffleExitPending)
                return;

            if (_seatShuffleVehicleHandle == 0 || !NativeCompat.DoesEntityExist(_seatShuffleVehicleHandle))
            {
                ClearSeatShuffleExitGuard();
                return;
            }

            if (playerInVehicle)
            {
                if (currentVeh == null || !currentVeh.Exists() || currentVeh.Handle != _seatShuffleVehicleHandle)
                {
                    ClearSeatShuffleExitGuard();
                    return;
                }
            }

            if (Game.GameTime > _seatShuffleExitUntilGameTime)
            {
                if (!playerInVehicle)
                    BeginPostExitBrakeClear(_seatShuffleVehicleHandle, true);

                ClearSeatShuffleExitGuard();
                return;
            }

            ProtectRunningVehicleNow(_seatShuffleVehicleHandle);

            if (!playerInVehicle)
                BeginPostExitBrakeClear(_seatShuffleVehicleHandle, true);
        }

        private void BeginOrRefreshSeatShuffleExitGuard(Vehicle veh)
        {
            if (veh == null || !veh.Exists())
                return;

            int vehHandle = veh.Handle;

            _seatShuffleExitPending = true;
            _seatShuffleVehicleHandle = vehHandle;
            _seatShuffleExitUntilGameTime = Game.GameTime + GetSeatShuffleExitEnforceMs(vehHandle);
        }

        private void ClearSeatShuffleExitGuard()
        {
            _seatShuffleExitPending = false;
            _seatShuffleVehicleHandle = 0;
            _seatShuffleExitUntilGameTime = 0;
        }

        private bool IsHeavyRoadVehicleHandle(int vehHandle)
        {
            if (vehHandle == 0 || !NativeCompat.DoesEntityExist(vehHandle))
                return false;

            try
            {
                int vehicleClass = Function.Call<int>(Hash.GET_VEHICLE_CLASS, vehHandle);
                return vehicleClass == 10 || vehicleClass == 11 || vehicleClass == 12 || vehicleClass == 17 || vehicleClass == 20;
            }
            catch
            {
                return false;
            }
        }

        private int GetSeatShuffleExitEnforceMs(int vehHandle)
        {
            int baseMs = IsHeavyRoadVehicleHandle(vehHandle) ? HeavyVehicleShuffleExitEnforceMs : SeatShuffleExitEnforceMs;
            return Math.Max(_exitEnforceMs, baseMs);
        }

        private int GetPostExitBrakeClearDurationMs(int vehHandle)
        {
            return IsHeavyRoadVehicleHandle(vehHandle) ? HeavyVehiclePostExitBrakeClearMs : PostExitBrakeClearMs;
        }

        private void BeginPostExitBrakeClear(int vehHandle, bool keepEngineOn)
        {
            if (vehHandle == 0)
                return;

            _postExitBrakeVehicleHandle = vehHandle;
            _postExitBrakeUntilGameTime = Game.GameTime + GetPostExitBrakeClearDurationMs(vehHandle);
            _postExitBrakeKeepEngineOn = keepEngineOn;
        }

        private void UpdatePostExitBrakeClear(Ped player)
        {
            if (_postExitBrakeVehicleHandle == 0)
                return;

            if (Game.GameTime > _postExitBrakeUntilGameTime)
            {
                try { NativeCompat.SetVehicleKeepEngineOnWhenAbandoned(_postExitBrakeVehicleHandle, false); } catch { }
                _postExitBrakeVehicleHandle = 0;
                _postExitBrakeUntilGameTime = 0;
                _postExitBrakeKeepEngineOn = false;
                return;
            }

            if (!NativeCompat.DoesEntityExist(_postExitBrakeVehicleHandle))
            {
                _postExitBrakeVehicleHandle = 0;
                _postExitBrakeUntilGameTime = 0;
                _postExitBrakeKeepEngineOn = false;
                return;
            }

            if (player != null && player.Exists() && player.IsInVehicle())
            {
                Vehicle playerVeh = player.CurrentVehicle;
                if (playerVeh != null && playerVeh.Exists() && playerVeh.Handle == _postExitBrakeVehicleHandle)
                {
                    try { NativeCompat.SetVehicleKeepEngineOnWhenAbandoned(_postExitBrakeVehicleHandle, false); } catch { }
                    _postExitBrakeVehicleHandle = 0;
                    _postExitBrakeUntilGameTime = 0;
                    _postExitBrakeKeepEngineOn = false;
                    return;
                }
            }

            if (_postExitBrakeKeepEngineOn)
                ProtectRunningVehicleNow(_postExitBrakeVehicleHandle);

            Function.Call(Hash.SET_VEHICLE_BRAKE, _postExitBrakeVehicleHandle, false);
            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, _postExitBrakeVehicleHandle, false);
            Function.Call(Hash.SET_VEHICLE_BRAKE_LIGHTS, _postExitBrakeVehicleHandle, false);
        }

        private void ApplyExitEnforcement()
        {
            if (!_exitArmed)
                return;

            if (BusForcesOff())
            {
                if (_exitVehicleHandle != 0 && NativeCompat.DoesEntityExist(_exitVehicleHandle))
                    NativeCompat.SetVehicleKeepEngineOn(_exitVehicleHandle, false);
                _exitArmed = false;
                _exitVehicleHandle = 0;
                return;
            }

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

            if (_maxDistanceToTrackFeet > 0f)
            {
                try
                {
                    Ped p = Game.Player.Character;
                    Vehicle v = VehicleFromHandle(_exitVehicleHandle);
                    if (p != null && p.Exists() && v != null && v.Exists())
                    {
                        float distFeet = p.Position.DistanceTo(v.Position) * FeetPerMeter;
                        if (distFeet > _maxDistanceToTrackFeet)
                        {
                            NativeCompat.SetVehicleKeepEngineOn(_exitVehicleHandle, false);
                            LogInfoThrottled("mp_exit_cancel_dist", $"Exit enforcement canceled (distance). veh={_exitVehicleHandle} distFeet={distFeet:0} > max={_maxDistanceToTrackFeet:0}.", 750);

                            _exitArmed = false;
                            _exitVehicleHandle = 0;
                            return;
                        }
                    }
                }
                catch { }
            }

            ProtectRunningVehicleNow(_exitVehicleHandle);

            if (Game.Player.Character != null && Game.Player.Character.Exists() && !Game.Player.Character.IsInVehicle())
                BeginPostExitBrakeClear(_exitVehicleHandle, true);
        }

        private static bool BusForcesOff()
        {
            try
            {
                return EngineOverrideBus.GetCurrent(out _, out _, out _) == EngineIntent.ForceOff;
            }
            catch
            {
                return false;
            }
        }

        private static void ForceEngineOnHard(int vehHandle)
        {
            try { Function.Call(Hash.SET_VEHICLE_UNDRIVEABLE, vehHandle, false); } catch { }
            try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehHandle, true, true, false); } catch { }
            try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehHandle, true, false, true); } catch { }
        }

        private static void ProtectRunningVehicleNow(int vehHandle)
        {
            try { NativeCompat.SetVehicleKeepEngineOn(vehHandle, true); } catch { }
            try { NativeCompat.SetVehicleKeepEngineOnWhenAbandoned(vehHandle, true); } catch { }
            try { Function.Call(Hash.SET_VEHICLE_UNDRIVEABLE, vehHandle, false); } catch { }
            try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehHandle, true, true, false); } catch { }
            try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehHandle, true, false, true); } catch { }
            try { Function.Call(Hash.SET_VEHICLE_BRAKE, vehHandle, false); } catch { }
            try { Function.Call(Hash.SET_VEHICLE_HANDBRAKE, vehHandle, false); } catch { }
            try { Function.Call(Hash.SET_VEHICLE_BRAKE_LIGHTS, vehHandle, false); } catch { }
        }

        private void MaintainKeepEngineOnFlag(Ped player, Vehicle currentVeh)
        {
            if (BusForcesOff())
            {
                ClearKeepEngineOnFlagIfAny();
                return;
            }

            if (currentVeh == null || !currentVeh.Exists())
            {
                ClearKeepEngineOnFlagIfAny();
                return;
            }

            bool treatAsDriver =
                (currentVeh.Driver == player) ||
                (currentVeh.Handle == _recentDriverVehicleHandle && (Game.GameTime - _recentDriverGameTime) <= RecentDriverGraceMs);

            if (!treatAsDriver)
            {
                ClearKeepEngineOnFlagIfAny();
                return;
            }

            if (IsAircraft(currentVeh))
            {
                ClearKeepEngineOnFlagIfAny();
                return;
            }

            int h = currentVeh.Handle;

            EngineIntent busIntent = EngineOverrideBus.GetCurrent(out _, out _, out _);
            if (busIntent == EngineIntent.ForceOff)
            {
                ClearKeepEngineOnFlagIfAny();
                return;
            }

            bool engineOnNow = NativeCompat.IsVehicleEngineOn(h);
            bool engineWasOnVeryRecently = (_lastVehicleHandle == h) && (Game.GameTime - _lastEngineOnGameTime) <= 1500;

            bool inSeatShuffleGrace =
                (currentVeh.Driver != player) &&
                (h == _recentDriverVehicleHandle) &&
                (Game.GameTime - _recentDriverGameTime) <= RecentDriverGraceMs;

            bool shouldKeep = engineOnNow || (inSeatShuffleGrace && (engineWasOnVeryRecently || _recentDriverEngineWasOn));

            if (!shouldKeep)
            {
                ClearKeepEngineOnFlagIfAny();
                return;
            }

            if (_keepEngineOnVehicleHandle != 0 && _keepEngineOnVehicleHandle != h && NativeCompat.DoesEntityExist(_keepEngineOnVehicleHandle))
                NativeCompat.SetVehicleKeepEngineOn(_keepEngineOnVehicleHandle, false);

            _keepEngineOnVehicleHandle = h;
            NativeCompat.SetVehicleKeepEngineOn(h, true);

            if (!engineOnNow && inSeatShuffleGrace && (engineWasOnVeryRecently || _recentDriverEngineWasOn))
            {
                ForceEngineOnHard(h);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, h, true, false, true);
            }
        }

        private void ClearKeepEngineOnFlagIfAny()
        {
            if (_keepEngineOnVehicleHandle == 0)
                return;

            if (NativeCompat.DoesEntityExist(_keepEngineOnVehicleHandle))
            {
                NativeCompat.SetVehicleKeepEngineOn(_keepEngineOnVehicleHandle, false);
                NativeCompat.SetVehicleKeepEngineOnWhenAbandoned(_keepEngineOnVehicleHandle, false);
            }

            _keepEngineOnVehicleHandle = 0;
        }

        private void LoadIniOnce()
        {
            try
            {
                LogInfo($"INI: LoadIniOnce path={MainConfig.IniPathUsed} exists={MainConfig.IniExists}");

                string sec = "(Main.cs)";
                string raw = "(shared)";

                _disableAutoStart = MainConfig.DisableAutoStart;
                LogInfo($"INI: DisableAutoStart section={sec} raw='{raw}' -> {_disableAutoStart}");

                _disableAutoShutdown = MainConfig.DisableAutoShutdown;
                LogInfo($"INI: DisableAutoShutdown section={sec} raw='{raw}' -> {_disableAutoShutdown}");

                _entryEnforceMs = MainConfig.EntryEnforceMs;
                LogInfo($"INI: EntryEnforceMs section={sec} raw='{raw}' -> {_entryEnforceMs}");

                _exitEnforceMs = MainConfig.ExitEnforceMs;
                LogInfo($"INI: ExitEnforceMs section={sec} raw='{raw}' -> {_exitEnforceMs}");

                _maxDistanceToTrackFeet = MainConfig.MaxDistanceToTrack;
                LogInfo($"INI: MaxDistanceToTrack section={sec} raw='{raw}' -> {_maxDistanceToTrackFeet}");

                _maxTrackedVehicle = MainConfig.MaxTrackedVehicle;
                LogInfo($"INI: MaxTrackedVehicle section={sec} raw='{raw}' -> {_maxTrackedVehicle}");
            }
            catch (Exception ex)
            {
                LogError("Failed to load EngineStateManager.ini (using defaults).", ex);
            }
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min)
                return min;
            if (v > max)
                return max;
            return v;
        }

        private static bool IsAircraft(Vehicle v)
        {
            try
            {
                if (v == null || !v.Exists())
                    return false;

                Model m = v.Model;
                return m.IsPlane || m.IsHelicopter;
            }
            catch
            {
                return false;
            }
        }

        private bool IsCarPersistenceEnabled()
        {
            if (!_disableAutoShutdown)
                return false;

            if (_maxDistanceToTrackFeet == 0f)
                return false;

            if (_maxTrackedVehicle <= 0)
                return false;

            return true;
        }

        private void TouchPersistedVehicle(int vehHandle)
        {
            if (vehHandle == 0)
                return;

            if (!NativeCompat.DoesEntityExist(vehHandle))
                return;

            Vehicle v = VehicleFromHandle(vehHandle);
            if (v == null || !v.Exists())
                return;

            if (IsAircraft(v))
                return;

            if (_persistedSet.Contains(vehHandle))
            {
                LinkedListNode<int> node = _persistedLru.Find(vehHandle);
                if (node != null)
                {
                    _persistedLru.Remove(node);
                    _persistedLru.AddLast(node);
                }
            }
            else
            {
                _persistedSet.Add(vehHandle);
                _persistedLru.AddLast(vehHandle);
            }

            EvictIfOverLimit();
        }

        private void MaintainPersistedVehicles(Ped player)
        {
            if (player == null || !player.Exists())
                return;

            if (!IsCarPersistenceEnabled())
            {
                ClearAllPersistedVehicles();
                return;
            }

            LinkedListNode<int> node = _persistedLru.First;
            while (node != null)
            {
                LinkedListNode<int> next = node.Next;
                int handle = node.Value;
                bool remove = false;

                if (handle == 0 || !NativeCompat.DoesEntityExist(handle))
                {
                    remove = true;
                }
                else
                {
                    Vehicle v = VehicleFromHandle(handle);
                    if (v == null || !v.Exists() || IsAircraft(v))
                    {
                        remove = true;
                    }
                    else if (_maxDistanceToTrackFeet > 0f)
                    {
                        try
                        {
                            float distFeet = player.Position.DistanceTo(v.Position) * FeetPerMeter;
                            if (distFeet > _maxDistanceToTrackFeet)
                            {
                                NativeCompat.SetVehicleKeepEngineOn(handle, false);
                                LogInfoThrottled("mp_car_cancel_dist", $"Car untracked (distance). veh={handle} distFeet={distFeet:0} > max={_maxDistanceToTrackFeet:0}.", 1000);
                                remove = true;
                            }
                        }
                        catch { }
                    }
                }

                if (remove)
                {
                    _persistedSet.Remove(handle);
                    _persistedLru.Remove(node);
                }

                node = next;
            }

            EvictIfOverLimit();
        }

        private void ClearAllPersistedVehicles()
        {
            try
            {
                foreach (int handle in _persistedSet)
                {
                    try
                    {
                        if (handle != 0 && NativeCompat.DoesEntityExist(handle))
                            NativeCompat.SetVehicleKeepEngineOn(handle, false);
                    }
                    catch { }
                }
            }
            catch { }

            _persistedSet.Clear();
            _persistedLru.Clear();
        }

        private void EvictIfOverLimit()
        {
            int cap = _maxTrackedVehicle;
            if (cap < 1)
                return;

            while (_persistedSet.Count > cap && _persistedLru.First != null)
            {
                int oldest = _persistedLru.First.Value;
                _persistedLru.RemoveFirst();
                _persistedSet.Remove(oldest);

                try
                {
                    if (oldest != 0 && NativeCompat.DoesEntityExist(oldest))
                        NativeCompat.SetVehicleKeepEngineOn(oldest, false);
                }
                catch { }

                LogInfoThrottled("mp_car_evict", $"Car evicted (LRU cap={cap}). veh={oldest}.", 1000);
            }
        }

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

                if (ex != null)
                    EngineStateManager.ModLogger.Error(msg, ex);
                else
                    EngineStateManager.ModLogger.Error(msg);
            }
            catch { }
        }

        private static Vehicle VehicleFromHandle(int handle)
        {
            if (handle == 0)
                return null;

            try
            {
                return Entity.FromHandle(handle) as Vehicle;
            }
            catch
            {
                return null;
            }
        }
    }
}
