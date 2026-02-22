# EngineStateManager
### [Watch Showcase Video Here](https://www.youtube.com/watch?v=lPuOtOzJlYk)
GTA V ScriptHookVDotNet mod that gives players full control over vehicle engine behavior across all vehicle types.

By default, GTA V  auto-starts and auto-shuts engines during enter/exit animations, breaking immersion and realism. EngineStateManager overrides these behaviors using native-level logic, allowing engines to persist while giving the player manual authority over engine state.

Aircraft, helicopters, and ground vehicles are all supported.

Every feature is modular and fully configurable via INI.

**[ NOTE ]**
**PLANE ENGINE PERSISTENCE IS EXPERIMENTAL AND NOT RECOMMENDED USE IN A REGULAR PLAYTHROUGH!**
**THIS MIGHT CHANGE WITH FUTURE UPDATES.**

## **[ FEATURES ]**
#### **-** Prevents engines from shutting off when exiting vehicles
#### **-** Blocks GTA V’s automatic engine startup that works across all supported vehicle classes
#### **-** Eliminates RPM/audio dips during enter/exit animations
#### **-** Toggle vehicle engines on/off via hotkey that optionally includes animations
#### **-** Does not interfere with GTA’s default controls unless enabled
#### **-** Includes native fail-safe handling 
#### **-** Optional in-game notification when the mod loads
#### **-** Tracks vehicle engine state per entity
#### **-** Troubleshooting debug logging (if enabled)
#### **-** Designed for future GTA updates

### **[ Configuration Options ]**
All features are configurable via: **EngineStateManager.ini**

You can fully customize behavior without touching code.

Changes only apply on next script reload.

### [ Requirements ]
#### **-** [Latest ScriptHookV](https://www.dev-c.com/gtav/scripthookv/)
#### **-** Latest **ScriptHookVDotNet v3** for [**Enhanced**](https://www.gta5-mods.com/tools/script-hook-v-net-enhanced) or [**Legacy**](https://www.gta5-mods.com/tools/scripthookv-net)

### [ Known Issues ]
**Planes engine persistence works, but it has lots of inconsistencies that I havn't been able to fix yet.
Exiting a plane with the engine running (whether it's a Jet or Prop plane) briefly dips engine RPM then picks the rpm back up again. Same goes for entering a plane that has it's engine running.
Jet planes don't idle at they're idle speed when exiting them.**

**Nativily prop planes are impossible to keep running when exiting them (unless a prop plane rotor hash exists but has been hidden by rockstar), so I've decided to do a temporary work around by spawning an
invisible ped in the prop plane the player was last in to keep the engine idling.**
#### **These are the main reasons that the EnablePlanePersistence ini option is false by default.**
#### **If you encounter issues, please include your EngineStateManager.log when reporting.**

### **[ Planned Upcoming Features ]**
#### **-** _Per-vehicle persistence profiles_
#### **-** _Separate aircraft logic modes_
#### **-** _Advanced idle RPM control_
#### **-** _Expanded animation handling_
#### **-** _Additional realism options_



## [ Changelog ]
### Version 1.0.0
Initial Release
