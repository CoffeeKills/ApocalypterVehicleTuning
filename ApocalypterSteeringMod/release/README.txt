Apocalypter Vehicle Tuning 0.11.3-alpha
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
  the readouts you pick in Settings > "Strip contents" (speed, RPM,
  gear, slip, g-forces, steering angle, throttle, brakes). Prefer the
  old paused panel? Turn on "Freeze game while open" in the Settings
  tab.
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
- New in 0.11.3: the Weight tab's two numbers are now truly separate -
  the coarse row shows the coarse part and the trim row the trim part,
  and the applied weight is them added together. Dragging trim no
  longer rewrites the coarse readout or nudges the coarse handle (and
  a coarse drag never touches the trim number). Display-only change.
- New in 0.11.2: the Weight tab's trim sliders are fixed - dragging one
  used to move the coarse slider, fight the mouse and ratchet the weight
  to +-10000 kg within seconds (the +-100 kg slider range vs the +-50 kg
  trim domain flipped the 100 kg bucket on every drag event). The trim
  sliders now run +-50 kg and the value is clamped, so a drag held at
  the end stays put.
- New in 0.11.1: ballast no longer re-scales the springs - 0.10.0's
  rescale held the ride height constant, so positive ballast (real
  mass) never visibly compressed the suspension. The springs stay
  stock now: the body squats under load, and the balloon lift offsets
  the extreme loads.
- New in 0.11.0: the Weight tab's balloon lift now actually lifts
  (0.10.0's lift never ran - it is applied per physics tick by the mod
  itself now, uncapped), the weight range is -10 t to +20 t, and each
  axle has two sliders: coarse (100 kg steps) + trim (+-50 kg since
  0.11.2; the 0.11.0 range was +-100).
- New in 0.10.0: the Weight tab - front/rear ballast in kg (real
  mass, centre of mass and inertia) or negative-kg "balloon" lift at
  that axle. Presets: Front ballast, Rear ballast, Full load, Lift.
  Works with per-vehicle tunes. (0.10.0-0.11.0 also re-scaled the
  springs with the mass; 0.11.1 removed that - see above.)
- New in 0.9.0: per-vehicle tunes - save a tune under a vehicle's
  name and it re-applies to that vehicle only. Config-level for now:
  one line per tune in the config file's [PerVehicle] Tunes section
  (Apocasetter edits it live; a panel UI comes later). Switching
  saves now resets the tuning by default (ResetOnSaveSwitch = true).
- New in 0.8.0: max steering angle slider (0 = each vehicle's own
  lock, up to 70 deg for drift-style extra angle) and save tracking -
  the mod detects the loaded save slot and can turn every category
  off when a different save loads.
- New in 0.7.7: quieter logging - the gearbox debug hooks are off by
  default (the stuck-in-neutral warning stays).
- New in 0.7.6: loading with a custom gearbox enabled is FIXED - the controller was hooking before the game wrote its real transmission type and followed the stale prefab default. It now adopts the game's type and delegate the tick they change.
- New in 0.7.5: loading the game with a custom gearbox enabled is being chased down - the mod now logs every shift hook and warns if a controller stays in neutral with the throttle held (one repro log will name the culprit).
- New in 0.7.4: the per-tab Reset buttons now match the reorganized tabs (the merged tabs reset both of their categories), and the telemetry pin feature is replaced by a readout list (speed, RPM, gear, front/rear slip, lateral/longitudinal g, steering angle, throttle, brakes - pick them in Settings > "Strip contents").
- New in 0.7.3: 12-gear gearboxes no longer get grabbed by the game's own skipping automatic (the launch-from-gear-8 bug; the mod's shift controller is re-installed the same tick the game flips transmission type).
- New in 0.7.2: pinned telemetry values render beside the main strip at every corner (overlap fix).
- New in 0.7.1: the panel is reorganized by car area - 8 tabs:
  Steering, Suspension, Wheels (alignment + tires), Drivetrain
  (engine/diffs/layout + gearbox), Brakes, Assists, Aero, Settings.
  The 6x6 drivetrain template is gone (the game has no 6x6 vehicles;
  old configs with 6x6 layouts still work).
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
