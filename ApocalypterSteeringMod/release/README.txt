Apocalypter Vehicle Tuning 0.7.0-alpha
======================================

An in-game tuning panel for every vehicle in Apocalypter: steering,
suspension, wheel alignment, aero, brakes, tire grip, drivetrain,
and ABS/TCS assists. All applied live, all persisted per
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
  "Freeze game while open" in the Settings tab.
- New in 0.6.0-0.6.2: wheel alignment (camber, caster, toe, position),
  custom drivetrain layouts (config-only: AWD, part-time 4x4, 6x6 wiring),
  panel width/size/transparency settings, a rebindable panel hotkey
  (Settings tab), an optional live telemetry strip (off by default),
  and copy/paste for presets. Gearbox tuning is coming soon - the
  game's shift logic fights resized gearboxes, so the tab shows a
  notice until the mod ships its own shifting.
- New in 0.6.3: the mod keeps out of the game's post-load spawn wave
  and survives a half-initialised vehicle - after loading a save,
  tuning appears ~5 seconds later.
- New in 0.6.4: telemetry keeps YOUR car with hands off the keys even
  parked next to other cars - a parked car's frozen input (handbrake
  left on) can no longer steal the strip or the "Last driven" tuning
  target. If the strip ever shows the wrong car, set Telemetry ->
  DebugPick = true in the mod config and send the "Telemetry pick:"
  lines from BepInEx\LogOutput.log.
- New in 0.7.0: gearbox tuning is fully unlocked for every
  transmission - the mod runs its own shift controller while gearbox
  changes are active (the "coming soon" notice is gone), the Truck
  12-gear preset, centre-diff mode + torque-split readout, a panel UI
  for custom drivetrain layouts, telemetry value pinning, a tighter
  panel, and several fixes (game-changed values no longer overwritten,
  selected-vehicle targeting, panel reset, 300 px width, tab labels,
  digit keys). In-game checklist: README section 10, items 38-46.
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
