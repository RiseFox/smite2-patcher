# SMITE 2 graphics settings map

Verified on SMITE 2 build OB45 (UE 5.5.4), October 2026. These are two passes through the in-game menu, diffing the save after each change.

## Where the settings live

| File | What is in it |
|---|---|
| `%LOCALAPPDATA%\SMITE2Alpha\Saved\SaveGames\HWGameUserSettings.sav` | All in-game graphics, as menu indices. This is what `UHWGameUserSettings::ApplyAllSettings` applies. |
| `%LOCALAPPDATA%\SMITE2Alpha\Saved\Config\Windows\GameUserSettings.ini` | `[ScalabilityGroups] sg.*` levels (mirrors the save), `bUseVSync`, `sg.GlobalIlluminationQuality` (no menu entry). `FrameRateLimit` here does **not** set the in-match cap. |
| SMITE backend (`OnGetPlayerSettings`) | UI and controls only: camera, cast modes, HUD, `LockCursorToScreen`, VFX opacity… No graphics. |

Not loaded by this build: `Saved/Config/Windows/Engine.ini` (no `Set CVar` lines from it; FFX section keeps defaults), `Scalability.ini` in the same folder (the game deletes it), `-ini:Engine:` overrides and `-ExecCmds=` (no readbacks, caps not applied).

## Save format

GVAS file; top-level property `SavedSettingsConfig`:

```
FString "SavedSettingsConfig"
FString "MapProperty"
int32   2                      // type parameters
FString "NameProperty", int32 0
FString "StrProperty",  int32 0
int32   size                   // bytes from NumKeysToRemove to the end of the pairs
uint8   flags
int32   NumKeysToRemove (0)
int32   NumEntries
NumEntries × (FString key, FString value)
```

FStrings are int32 length (including the null) plus ASCII bytes. The game **leaves out keys that hold the default value**, updates existing keys in place and appends new keys at the end. The patcher does the same, and its output matched game-written maps byte for byte. `GameUserSettings.ini` gets `;METADATA=(Diff=true, UseCommands=true)` once the game rewrites it.

## Values

| Key | Values | Evidence |
|---|---|---|
| `ShadowQuality`, `EffectsQuality`, `TextureQuality`, `ShadingQuality`, `ViewDistanceQuality`, `FoliageQuality`, `ReflectionQuality`, `AntiAliasingQuality`, `PostProcessQuality` | 0 Low, 1 Medium, 2 High | Shadows High removed the key and wrote `sg.ShadowQuality=2`; Shadows/Effects/Textures Medium wrote 1; View distance High removed the key (`sg.ViewDistanceQuality=2`). Default (key missing) is High for Shadow, Texture and View distance. |
| `DLSS` (DLSS Super Resolution) | 1 Quality, 2 Balanced, 4 Ultra Performance | Quality → 1 and Balanced → 2 by switching; 4 shown as "Ultra Performance" in the menu. 3 = Performance is inferred from the order and was not switched. 0 is untested. |
| `MaxFPS`, `LobbyMaxFPS` | 1 = 30, 2 = 60, 3 = 90, 4 = 120, 5 = 144, 6 = 240 | Menu list 30/60/90/120/144/240; 144 → 5, 240 → 6, lobby 90 = 3. 0 is a hidden entry, untested. |
| `ReflexMode` | 1 On (key missing), 2 the option after On | On removed the key; the next option wrote 2. |
| `AntiAliasingMethod` | 0 Off, 1 FXAA, 2 TAA, 3 MSAA, 4 TSR | Menu order equals the UE enum. DLSS runs as a sub-option on TAA. |
| `DLSSFrameGen` | `true` = On; Off = key missing | Turning FG off removed the key; map size 661 → 635 bytes, entries 26 → 25. |
| `FSRFrameGen` | `true` | Not switched. |

Other keys in the map: `Resolution` and `ScreenMode` (menu indices), `GlobalQuality`, audio volumes, `FPSEnabled`, `ShowNetworkStatusIndicators` and `SavedChangelistVersion`.

## Driver side (NVIDIA profile of `Hemingway-Win64-Shipping.exe`)

| Setting | ID | Patcher value |
|---|---|---|
| DLSS-SR override | `0x10E41E01` | 1 |
| DLSS-SR performance mode | `0x10AFB768` | 3 (snippet/game controlled, so the in-game mode applies) |
| DLSS-SR preset | `0x10E41DF3` | 11 (K) |
| DLSS-FG override | `0x10E41E03` | 1 (FG plans only) |
| DLSS-G mode | `0x10308298` | 2 (On) |
| DLSS-G multi-frame count | `0x104D6667` | 1 (2X) |
| Frame Rate Limiter (Max Frame Rate) | `0x10835002` | refresh × 0.97 (FG plans only) |

IDs and values come from `NvApiDriverSettings.h` in the official NVAPI SDK. The NVIDIA App's *Max Frame Rate 232* was read back as `0x10835002 = 232`. In a session with that cap and FG 2X, the log's frame counter held 116 real FPS (median, p95 and maximum).
