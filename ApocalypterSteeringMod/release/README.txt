Apocalypter Vehicle Tuning 0.6.0-alpha
======================================

An in-game tuning panel for every vehicle in Apocalypter: steering,
suspension, wheel alignment, aero, brakes, tire grip, drivetrain,
gearbox, and ABS/TCS assists. All applied live, all persisted per
setting.

In game: press F7 (or the "Vehicle Tuning" button in the pause menu).


INSTALL
-------

1. Install BepInEx 5.4.x (win_x64) for Apocalypter.
2. Copy ApocalypterSteeringMod.dll into:
       Apocalypter\BepInEx\plugins\
3. Copy ApocalypterSteeringMod.png next to the DLL.
   (Optional: it gives the mod its icon in the Apocasetter Mods menu.)
4. If you ever used the old "SteeringFix.dll", delete it - the two
   must never run together.
5. Start the game. Press F7 in game to open the panel.

That's it. Settings live in:
   Apocalypter\BepInEx\config\dev.apocalypter.tractionsteering.cfg


UPDATING FROM 0.2.0 / 0.3.0 / 0.4.0 / 0.5.0
-------------------------------------------

Just replace the DLL. Your settings carry over and convert
automatically (old preset names and curve settings migrate on load).


NOTES
-----

- Every tuning category starts OFF (opt-in). Flip a master switch and
  pick a preset, or draw your own curves in the steering tab.
- The panel keeps the game running while it is open (docked on the
  right) - tune and drive at the same time. The telemetry strip shows
  speed/RPM/gear/slip. Prefer the old paused panel? Turn on
  "Freeze game while open" in the Panel tab.
- New in 0.6.0: wheel alignment (camber, caster, toe, position),
  gearbox tuning (gear count, clutch, per-gear ratios), panel width /
  size / transparency settings, and copy/paste for presets.
- Steering tab: the two graphs are editable - click to add a point,
  drag to move, double-click to remove. The "Return to center" graph
  is what makes the Euro Truck preset hold its wheels when stopped
  and straighten out as you drive.
- Apocasetter (optional): install it and this mod appears in its Mods
  menu with every setting as a live editor.
- ApocaLanguage (optional): the panel translates automatically when a
  language pack covers its strings.

Trouble? Look at Apocalypter\BepInEx\LogOutput.log - the mod logs its
startup line and any errors there.
