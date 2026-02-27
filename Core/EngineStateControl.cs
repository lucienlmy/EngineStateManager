
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
using System;
using System.Windows.Forms;

using EngineStateManager;
public sealed class EngineStateControl : Script
{
    private enum EngineOverrideState
    {
        None = 0,
        ForceOn = 1,
        ForceOff = 2
    }

    // INI
    private static bool _enabled = true;
    private static bool _animationsEnabled = true;
    private static Keys _toggleKey = Keys.Z;


    // Mod load notification (logo overlay)
    private readonly ModLoadNotification _loadNotification = new ModLoadNotification();
    private EngineOverrideState _override = EngineOverrideState.None;
    private int _targetVehicleHandle = 0;

    private int _blockRestartUntilGameTime = 0;

    private bool _keyWasDown = false;
    private int _lastToggleGameTime = 0;

    // Safety: if Tick throws, log once and stop processing to avoid a "script running but dead" state.
    private bool _tickFaulted = false;

    // remember update For 1.2.0 asshat
    private const string AnimDict = "veh@std@ds@base";
    private const string AnimName = "change_station";
    private bool _pendingAnim = false;
    private int _animRequestUntilGameTime = 0;
    private int _queuedPedHandle = 0;
    private int _queuedAnimDuration = 650;

    public EngineStateControl()
    {
        LoadIni();


        _loadNotification.Initialize();
        Tick += OnTick;
        KeyDown += OnKeyDown;

        Interval = 0;

        LogInfo($"EngineStateControl loaded. Enabled={_enabled} Key={_toggleKey} Animations={_animationsEnabled}");
    }

    private static void LoadIni()
    {
        // Centralized INI load (Main.cs)
        _enabled = MainConfig.EngineToggleEnabled;
        _animationsEnabled = MainConfig.EngineToggleAnimations;

        string keyString = MainConfig.EngineToggleKeyString ?? "Z";
        if (!Enum.TryParse(keyString, true, out Keys parsed))
            parsed = Keys.Z;

        _toggleKey = parsed;
    }

    private void OnTick(object sender, EventArgs e)
    {
        if (_tickFaulted)
            return;

        try
        {
            _loadNotification.OnTick();

            if (!_enabled)
            {
                if (_override != EngineOverrideState.None || _targetVehicleHandle != 0)
                    ClearOverride("Feature disabled by INI.");

                _pendingAnim = false;
                _keyWasDown = false;
                return;
            }

            bool down = Game.IsKeyPressed(_toggleKey);
            if (down && !_keyWasDown)
            {
                if (Game.GameTime - _lastToggleGameTime > 150)
                {
                    if (!IsBlockedByUI())
                    {
                        _lastToggleGameTime = Game.GameTime;
                        ToggleForCurrentVehicle();
                    }
                }
            }
            _keyWasDown = down;

            if (_animationsEnabled)
                ProcessPendingAnim();

            EnforceOverrideIfNeeded();
        }
        catch (Exception ex)
        {
            _tickFaulted = true;
            try { LogInfo("FATAL: EngineStateControl Tick exception; disabling script loop. " + ex); } catch { }
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_enabled)
            return;

        if (e.KeyCode != _toggleKey)
            return;

        if (IsBlockedByUI())
            return;

        // Debounce and prevent double-trigger with Tick polling.
        if (Game.GameTime - _lastToggleGameTime <= 150)
            return;

        _lastToggleGameTime = Game.GameTime;
        ToggleForCurrentVehicle();
    }

    private void ToggleForCurrentVehicle()
    {
        Ped ped = Game.Player.Character;
        if (ped == null || !ped.Exists() || !ped.IsInVehicle())
            return;

        Vehicle veh = ped.CurrentVehicle;
        if (veh == null || !veh.Exists())
            return;

        _targetVehicleHandle = veh.Handle;

        bool running = IsEngineRunning(veh);

        if (_animationsEnabled)
            QueueToggleAnim(ped, turnOff: running);

        _override = running ? EngineOverrideState.ForceOff : EngineOverrideState.ForceOn;

        EngineOverrideBus.Set(
            _override == EngineOverrideState.ForceOff ? EngineIntent.ForceOff : EngineIntent.ForceOn,
            EngineIntentPriority.Critical,
            durationMs: 0, // indefinite until cleared
            owner: "EngineStateControl"
        );

        if (_override == EngineOverrideState.ForceOff)
            _blockRestartUntilGameTime = Game.GameTime + 500;
        else
            _blockRestartUntilGameTime = 0;

        ApplyOverrideToVehicle(veh, _override);

        LogInfo($"Toggle: Veh={_targetVehicleHandle} WasRunning={running} Override={_override}");
    }

    private void EnforceOverrideIfNeeded()
    {
        if (_override == EngineOverrideState.None || _targetVehicleHandle == 0)
            return;

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

        Vehicle current = ped.CurrentVehicle;
        if (current == null || !current.Exists())
        {
            ClearOverride("Current vehicle invalid.");
            return;
        }

        if (current.Handle != _targetVehicleHandle)
        {
            ClearOverride("Switched vehicles.");
            return;
        }

        if (_override == EngineOverrideState.ForceOff && IsEngineRunning(current))
        {
            ClearOverride("Engine started natively.");
            return;
        }

        if (_override == EngineOverrideState.ForceOff && Game.GameTime < _blockRestartUntilGameTime)
            return;

        ApplyOverrideToVehicle(current, _override);
    }

    private void ApplyOverrideToVehicle(Vehicle veh, EngineOverrideState state)
    {
        switch (state)
        {
            case EngineOverrideState.ForceOn:
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, veh.Handle, true, false, false);
                break;

            case EngineOverrideState.ForceOff:
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, veh.Handle, false, false, false);
                break;
        }
    }

    private void ClearOverride(string reason)
    {
        LogInfo($"ClearOverride: {reason}");

        EngineOverrideBus.Clear("EngineStateControl");

        _override = EngineOverrideState.None;
        _targetVehicleHandle = 0;
        _blockRestartUntilGameTime = 0;

    }

    private static bool IsEngineRunning(Vehicle veh)
        => Function.Call<bool>(Hash.GET_IS_VEHICLE_ENGINE_RUNNING, veh.Handle);

    private static bool IsBlockedByUI()
    {
        if (Game.IsPaused)
            return true;

        if (Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE))
            return true;

        int kb = Function.Call<int>(Hash.UPDATE_ONSCREEN_KEYBOARD);
        return kb == 0 || kb == 1;
    }

    // ---------- Animation ----------

    private void QueueToggleAnim(Ped ped, bool turnOff)
    {
        Function.Call(Hash.REQUEST_ANIM_DICT, AnimDict);

        _pendingAnim = true;
        _queuedPedHandle = ped.Handle;
        _queuedAnimDuration = turnOff ? 600 : 650;
        _animRequestUntilGameTime = Game.GameTime + 500;
    }

    private void ProcessPendingAnim()
    {
        if (!_pendingAnim)
            return;

        Ped ped = Game.Player.Character;
        if (ped == null || !ped.Exists() || ped.Handle != _queuedPedHandle)
        {
            _pendingAnim = false;
            return;
        }

        if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, AnimDict))
        {
            if (Game.GameTime <= _animRequestUntilGameTime)
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, AnimDict);
                return;
            }

            _pendingAnim = false;
            return;
        }

        Function.Call(Hash.TASK_PLAY_ANIM,
            ped.Handle,
            AnimDict,
            AnimName,
            8.0f,
            1.0f,
            _queuedAnimDuration,
            48,
            0.1f,
            false, false, false);

        _pendingAnim = false;
    }

    // ---------- Logging ----------

    private static void LogInfo(string msg)
    {
        try
        {
            if (EngineStateManager.ModLogger.Enabled)
                EngineStateManager.ModLogger.Info(msg);
        }
        catch { }
    }
}
