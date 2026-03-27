
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
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GTAControl = GTA.Control;
using EngineStateManager;

public sealed class EngineStateControl : Script
{
    private enum EngineOverrideState
    {
        None = 0,
        ForceOn = 1,
        ForceOff = 2
    }

    private const string BusOwner = "EngineStateControl";
    private const int ConfigRefreshIntervalMs = 500;
    private const int ToggleDebounceMs = 150;
    private const int ForceOnSettleMs = 450;
    private const int GroundForceOffHoldMs = 300;
    private const int AircraftBusRefreshMs = 250;
    private const int InputHealthCheckIntervalMs = 1000;
    private const int TickExceptionLogCooldownMs = 1000;
    private const int StaleOnscreenKeyboardBlockTimeoutMs = 15000;

    private static bool _enabled = true;
    private static int _toggleVk = 0x5A;
    private static Keys _toggleKey = Keys.Z;
    private static bool _controllerEnabled;
    private static GTAControl _controllerMain = GTAControl.VehicleDuck;

    private readonly ModLoadNotification _loadNotification = new ModLoadNotification();

    private EngineOverrideState _override = EngineOverrideState.None;
    private int _targetVehicleHandle;
    private int _forceOnReleaseAtGameTime;
    private int _nativeRestartAllowedAtGameTime;
    private bool _persistentForceOffLatch;

    private bool _keyWasDown;
    private bool _controllerWasDown;
    private bool _uiWasBlocking;
    private bool _queuedKeyboardToggle;
    private int _onscreenKeyboardBlockStartedAtGameTime = -1;
    private bool _staleOnscreenKeyboardStateIgnored;

    private int _lastToggleGameTime;
    private int _nextConfigRefreshGameTime;
    private int _lastInputHealthCheckGameTime;
    private int _lastTickExceptionLogGameTime = -10000;

    public EngineStateControl()
    {
        RefreshBindingsFromConfig();
        _loadNotification.Initialize();

        Tick += OnTick;
        KeyDown += OnKeyDown;
        Interval = 0;

        LogInfo($"EngineStateControl loaded. Enabled={_enabled} KeyVK=0x{_toggleVk:X2} ({_toggleKey}) ControllerEnabled={_controllerEnabled} ControllerMain={_controllerMain}");
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static void RefreshBindingsFromConfig()
    {
        MainConfig.RefreshIfChanged();

        _enabled = MainConfig.EngineToggleEnabled;
        _toggleKey = ResolveToggleKey(MainConfig.EngineToggleMainVk, MainConfig.EngineToggleKeyString, out _toggleVk);
        _controllerEnabled = MainConfig.EngineToggleControllerEnabled;
        _controllerMain = MainConfig.EngineToggleControllerMain;
    }

    private static Keys ResolveToggleKey(int configuredVk, string configuredKeyString, out int resolvedVk)
    {
        if (configuredVk != 0)
        {
            resolvedVk = configuredVk;
            return (Keys)configuredVk;
        }

        string keyString = string.IsNullOrWhiteSpace(configuredKeyString) ? "Z" : configuredKeyString;
        Keys parsedKey;

        if (!Enum.TryParse(keyString, true, out parsedKey))
        {
            parsedKey = Keys.Z;
        }

        resolvedVk = (int)parsedKey;
        return parsedKey;
    }

    private void OnTick(object sender, EventArgs e)
    {
        try
        {
            if (Game.GameTime >= _nextConfigRefreshGameTime)
            {
                SyncRuntimeConfig();
                _nextConfigRefreshGameTime = Game.GameTime + ConfigRefreshIntervalMs;
            }

            _loadNotification.OnTick();

            if (!_enabled)
            {
                ClearOverride("Feature disabled by INI.");
                ResetInputEdges();
                return;
            }

            MaintainInputHealth();

            if (WasTogglePressedThisFrame())
            {
                int now = Game.GameTime;
                if (now - _lastToggleGameTime > ToggleDebounceMs)
                {
                    _lastToggleGameTime = now;
                    ToggleForCurrentVehicle();
                }
            }

            EnforceOverrideIfNeeded();
        }
        catch (Exception ex)
        {
            if (Game.GameTime - _lastTickExceptionLogGameTime >= TickExceptionLogCooldownMs)
            {
                _lastTickExceptionLogGameTime = Game.GameTime;

                try
                {
                    LogInfo("Recovered from EngineStateControl tick exception: " + ex);
                }
                catch
                {
                }
            }
        }
    }

    private void SyncRuntimeConfig()
    {
        MainConfig.RefreshIfChanged();

        bool newEnabled = MainConfig.EngineToggleEnabled;
        int newToggleVk;
        Keys newToggleKey = ResolveToggleKey(MainConfig.EngineToggleMainVk, MainConfig.EngineToggleKeyString, out newToggleVk);
        bool newControllerEnabled = MainConfig.EngineToggleControllerEnabled;
        GTAControl newControllerMain = MainConfig.EngineToggleControllerMain;

        bool bindingsChanged =
            newToggleVk != _toggleVk ||
            newControllerEnabled != _controllerEnabled ||
            newControllerMain != _controllerMain;

        _enabled = newEnabled;
        _toggleVk = newToggleVk;
        _toggleKey = newToggleKey;
        _controllerEnabled = newControllerEnabled;
        _controllerMain = newControllerMain;

        if (bindingsChanged)
        {
            ResetInputEdges();
            LogInfo($"Bindings refreshed. KeyVK=0x{_toggleVk:X2} ({_toggleKey}) ControllerEnabled={_controllerEnabled} ControllerMain={_controllerMain}");
        }
    }

    private void ResetInputEdges()
    {
        _keyWasDown = false;
        _controllerWasDown = false;
        _uiWasBlocking = false;
        _queuedKeyboardToggle = false;
    }

    private void ResetOnscreenKeyboardBlockTracking()
    {
        _onscreenKeyboardBlockStartedAtGameTime = -1;
        _staleOnscreenKeyboardStateIgnored = false;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (!_enabled || IsBlockedByUI())
            {
                return;
            }

            if (e != null && e.KeyCode == _toggleKey)
            {
                _queuedKeyboardToggle = true;
            }
        }
        catch
        {
        }
    }

    private void MaintainInputHealth()
    {
        int now = Game.GameTime;
        if (now - _lastInputHealthCheckGameTime < InputHealthCheckIntervalMs)
        {
            return;
        }

        _lastInputHealthCheckGameTime = now;

        if (IsBlockedByUI())
        {
            ResetInputEdges();
            return;
        }

        bool keyboardDown = IsKeyboardToggleCurrentlyDown();
        bool controllerDown = IsControllerToggleCurrentlyDown();

        if (!keyboardDown)
        {
            _keyWasDown = false;
        }

        if (!controllerDown)
        {
            _controllerWasDown = false;
        }
    }

    private bool WasTogglePressedThisFrame()
    {
        if (IsBlockedByUI())
        {
            ResetInputEdges();
            _uiWasBlocking = true;
            return false;
        }

        bool keyboardDown = IsKeyboardToggleCurrentlyDown();
        bool controllerDown = IsControllerToggleCurrentlyDown();
        bool queuedKeyboardToggle = _queuedKeyboardToggle;
        _queuedKeyboardToggle = false;

        if (_uiWasBlocking)
        {
            _keyWasDown = keyboardDown;
            _controllerWasDown = controllerDown;
            _uiWasBlocking = false;
            return false;
        }

        bool keyboardEdge = keyboardDown && !_keyWasDown;
        bool controllerEdge = controllerDown && !_controllerWasDown;
        bool trigger = queuedKeyboardToggle || keyboardEdge || controllerEdge;

        _keyWasDown = keyboardDown;
        _controllerWasDown = controllerDown;

        return trigger;
    }

    private void ToggleForCurrentVehicle()
    {
        Ped ped = Game.Player.Character;
        if (ped == null || !ped.Exists() || !ped.IsInVehicle())
        {
            return;
        }

        Vehicle vehicle = ped.CurrentVehicle;
        if (vehicle == null || !vehicle.Exists())
        {
            return;
        }

        if (!IsDriverOfVehicle(ped, vehicle))
        {
            return;
        }

        bool isRunning = IsEngineRunning(vehicle);

        _targetVehicleHandle = vehicle.Handle;
        _override = isRunning ? EngineOverrideState.ForceOff : EngineOverrideState.ForceOn;

        if (_override == EngineOverrideState.ForceOn)
        {
            _persistentForceOffLatch = false;
            _forceOnReleaseAtGameTime = Game.GameTime + ForceOnSettleMs;
            _nativeRestartAllowedAtGameTime = 0;
            EngineOverrideBus.Set(EngineIntent.ForceOn, EngineIntentPriority.High, 0, BusOwner);
        }
        else
        {
            _persistentForceOffLatch = IsAircraft(vehicle);
            _forceOnReleaseAtGameTime = 0;
            _nativeRestartAllowedAtGameTime = _persistentForceOffLatch ? 0 : Game.GameTime + GroundForceOffHoldMs;
            EngineOverrideBus.Set(
                EngineIntent.ForceOff,
                EngineIntentPriority.High,
                _persistentForceOffLatch ? AircraftBusRefreshMs : GroundForceOffHoldMs,
                BusOwner);
        }

        ApplyOverrideToVehicle(vehicle, _override);
        LogInfo($"Toggle: Veh={_targetVehicleHandle} WasRunning={isRunning} Override={_override} PersistentAircraftLatch={_persistentForceOffLatch}");
    }

    private void EnforceOverrideIfNeeded()
    {
        if (_override == EngineOverrideState.None || _targetVehicleHandle == 0)
        {
            return;
        }

        string busOwner;
        EngineIntentPriority busPriority;
        int busExpiresAt;
        EngineIntent intent = EngineOverrideBus.GetCurrent(out busOwner, out busPriority, out busExpiresAt);

        if (intent != EngineIntent.None && !string.Equals(busOwner, BusOwner, StringComparison.Ordinal))
        {
            ClearOverride("Yielding to another override owner: " + busOwner, false);
            return;
        }

        Ped ped = Game.Player.Character;
        if (ped == null || !ped.Exists())
        {
            ClearOverride("Player ped invalid.");
            return;
        }

        if (!ped.IsInVehicle())
        {
            ClearOverride("Player left vehicle.");
            return;
        }

        Vehicle currentVehicle = ped.CurrentVehicle;
        if (currentVehicle == null || !currentVehicle.Exists())
        {
            ClearOverride("Current vehicle invalid.");
            return;
        }

        if (currentVehicle.Handle != _targetVehicleHandle)
        {
            ClearOverride("Switched vehicles.");
            return;
        }

        if (!IsDriverOfVehicle(ped, currentVehicle))
        {
            ClearOverride("No longer driver.");
            return;
        }

        if (_override == EngineOverrideState.ForceOn)
        {
            ApplyOverrideToVehicle(currentVehicle, EngineOverrideState.ForceOn);

            if (Game.GameTime >= _forceOnReleaseAtGameTime && IsEngineRunning(currentVehicle))
            {
                ClearOverride("ForceOn settled.");
            }

            return;
        }

        if (_persistentForceOffLatch)
        {
            if (HasNativeRestartIntent())
            {
                ClearOverride("Aircraft restart intent.");
                NativeCompat.SetVehicleEngineOn(currentVehicle.Handle, true, false, true);
                return;
            }

            EngineOverrideBus.Set(EngineIntent.ForceOff, EngineIntentPriority.High, AircraftBusRefreshMs, BusOwner);
            ApplyOverrideToVehicle(currentVehicle, EngineOverrideState.ForceOff);
            return;
        }

        if (Game.GameTime < _nativeRestartAllowedAtGameTime)
        {
            if (IsEngineRunning(currentVehicle))
            {
                ApplyOverrideToVehicle(currentVehicle, EngineOverrideState.ForceOff);
            }

            return;
        }

        ClearOverride("ForceOff settled.");

        if (HasNativeRestartIntent())
        {
            NativeCompat.SetVehicleEngineOn(currentVehicle.Handle, true, false, true);
        }
    }

    private static void ApplyOverrideToVehicle(Vehicle vehicle, EngineOverrideState state)
    {
        if (vehicle == null || !vehicle.Exists())
        {
            return;
        }

        if (state == EngineOverrideState.ForceOn)
        {
            NativeCompat.SetVehicleEngineOn(vehicle.Handle, true, false, true);
        }
        else if (state == EngineOverrideState.ForceOff)
        {
            NativeCompat.SetVehicleEngineOn(vehicle.Handle, false, true, true);
        }
    }

    private void ClearOverride(string reason, bool clearBus = true)
    {
        if (_override == EngineOverrideState.None && _targetVehicleHandle == 0)
        {
            return;
        }

        LogInfo($"ClearOverride: {reason}");

        if (clearBus)
        {
            EngineOverrideBus.Clear(BusOwner);
        }

        _override = EngineOverrideState.None;
        _targetVehicleHandle = 0;
        _forceOnReleaseAtGameTime = 0;
        _nativeRestartAllowedAtGameTime = 0;
        _persistentForceOffLatch = false;
    }

    private static bool IsEngineRunning(Vehicle vehicle)
    {
        return vehicle != null && vehicle.Exists() && NativeCompat.IsVehicleEngineOn(vehicle.Handle);
    }

    private static bool IsAircraft(Vehicle vehicle)
    {
        if (vehicle == null || !vehicle.Exists())
        {
            return false;
        }

        int vehicleClass = Function.Call<int>(Hash.GET_VEHICLE_CLASS, vehicle.Handle);
        return vehicleClass == 15 || vehicleClass == 16;
    }

    private static bool IsDriverOfVehicle(Ped ped, Vehicle vehicle)
    {
        if (ped == null || !ped.Exists() || vehicle == null || !vehicle.Exists())
        {
            return false;
        }

        try
        {
            return vehicle.GetPedOnSeat(VehicleSeat.Driver) == ped;
        }
        catch
        {
            return false;
        }
    }

    private bool IsBlockedByUI()
    {
        if (Game.IsPaused)
        {
            ResetOnscreenKeyboardBlockTracking();
            return true;
        }

        bool pauseMenuActive;
        try
        {
            pauseMenuActive = Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE);
        }
        catch
        {
            pauseMenuActive = false;
        }

        if (pauseMenuActive)
        {
            ResetOnscreenKeyboardBlockTracking();
            return true;
        }

        int keyboardState;
        try
        {
            keyboardState = Function.Call<int>(Hash.UPDATE_ONSCREEN_KEYBOARD);
        }
        catch
        {
            ResetOnscreenKeyboardBlockTracking();
            return false;
        }

        bool blockingState = keyboardState == 0 || keyboardState == 1;
        if (!blockingState)
        {
            ResetOnscreenKeyboardBlockTracking();
            return false;
        }

        int now = Game.GameTime;
        if (_onscreenKeyboardBlockStartedAtGameTime < 0)
        {
            _onscreenKeyboardBlockStartedAtGameTime = now;
            _staleOnscreenKeyboardStateIgnored = false;
            return true;
        }

        if ((now - _onscreenKeyboardBlockStartedAtGameTime) < StaleOnscreenKeyboardBlockTimeoutMs)
        {
            return true;
        }

        if (!_staleOnscreenKeyboardStateIgnored)
        {
            _staleOnscreenKeyboardStateIgnored = true;
            LogInfo("Ignoring stale onscreen keyboard state so engine toggle input cannot get stuck blocked.");
        }

        return false;
    }

    private static bool IsKeyboardToggleCurrentlyDown()
    {
        bool shvdnDown = false;

        try
        {
            shvdnDown = Game.IsKeyPressed(_toggleKey);
        }
        catch
        {
        }

        if (shvdnDown)
        {
            return true;
        }

        if (_toggleVk <= 0 || _toggleVk > 0xFF)
        {
            return false;
        }

        try
        {
            return (GetAsyncKeyState(_toggleVk) & 0x8000) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsControllerToggleCurrentlyDown()
    {
        if (!_controllerEnabled)
        {
            return false;
        }

        try
        {
            if (Game.IsControlPressed(_controllerMain))
            {
                return true;
            }
        }
        catch
        {
        }

        try
        {
            return
                Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)_controllerMain) ||
                Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 2, (int)_controllerMain);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasNativeRestartIntent()
    {
        try
        {
            if (Game.IsControlPressed(GTA.Control.VehicleAccelerate) || Game.IsControlPressed(GTA.Control.VehicleBrake))
            {
                return true;
            }
        }
        catch
        {
        }

        try
        {
            return
                Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)GTA.Control.VehicleAccelerate) ||
                Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)GTA.Control.VehicleBrake) ||
                Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 2, (int)GTA.Control.VehicleAccelerate) ||
                Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 2, (int)GTA.Control.VehicleBrake);
        }
        catch
        {
            return false;
        }
    }

    private static void LogInfo(string message)
    {
        try
        {
            if (ModLogger.Enabled)
            {
                ModLogger.Info(message);
            }
        }
        catch
        {
        }
    }
}
