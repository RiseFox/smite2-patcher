SMITE 2 Patcher — one-click optimization for NVIDIA RTX cards
==============================================================
https://github.com/RiseFox/smite2-patcher
(Українська інструкція — у файлі README-UA.txt)

HOW TO USE (once)

1. Unzip the archive anywhere, for example to your desktop.
   SMITE2-Patcher.exe and SmitePatchNv.dll must stay in the same folder.
2. Close SMITE 2.
3. Run SMITE2-Patcher.exe and click "Optimize".
   • If Windows shows "Windows protected your PC", click
     "More info" → "Run anyway".
   • If Steam is open, the patcher asks to close it for a few seconds.
     Say yes — it closes Steam and starts it again by itself.
   • The language follows Windows. Switch with "English / Українська"
     in the top-right corner of the window.
4. The patcher sets the in-game graphics for you. At the bottom of the
   window you get two tips for the game menu:
   VSync — Off and Lock Cursor to Window — Locked.

Then just launch the game as usual. You will not need the patcher again.


WHAT EXACTLY CHANGES

• The SMITE 2 profile in the NVIDIA driver:
  – the newer DLSS model (Preset K);
  – frame generation at exactly 2X — only on RTX 40/50 with a monitor
    of 200 Hz or more (at 90–165 Hz it would cut real frames, so it stays off);
  – together with frame generation, a frame cap just below the monitor
    refresh rate (for example 232 for 240 Hz). Without frame generation
    FPS is not capped: more frames, lower latency.
• In-game graphics — a preset for your graphics card
  (light / medium / high): shadows, effects, textures, DLSS, Reflex and
  the FPS limit. Laptop GPUs get one level lighter.
  You can change anything in the game menu afterwards.
• SMITE 2 launch option in Steam: -XeFGOverrideSwapChainDisabled
  (turns off Intel XeFG, which can stop DLSS from working).

No game files in the Steam folder are changed — only the game's settings
(with a backup). The patcher detects your card, monitor and driver itself.


CHECK THAT DLSS REALLY WORKS

Play one match or a practice game, quit and open the patcher again.
The "Last game" row shows what that session used:
  NVIDIA ✓ · DLSS ✓ 1280×720 → 1920×1080 · Intel XeFG disabled ✓
Green means all good. Red or "DLSS did not start" — send a screenshot of
the window.


UNDO EVERYTHING

Run the patcher and click "Restore everything".


WHEN TO RUN IT AGAIN

• You changed your monitor or graphics card.
• After an NVIDIA driver update the patcher says "not optimized yet".
• Do not press "Optimize" for SMITE 2 in the NVIDIA App — it can
  overwrite these settings.


TIPS

• Do not Alt-Tab during a match: frame generation restarts and the game
  freezes for a moment.
• After a game or driver update, start with a practice match: the first
  time gods and skins load, shaders get compiled (short hitches).
• Optional: update the game's DLSS files with DLSS Swapper:
  https://github.com/beeradmoore/dlss-swapper
  The patcher already makes the driver use the newest DLSS model, so this
  is optional. DLSS Swapper replaces DLLs in the game folder; Steam's
  "Verify integrity of game files" restores the originals. In an online
  game this is at your own risk.

If something does not work, send a screenshot of the patcher window.
