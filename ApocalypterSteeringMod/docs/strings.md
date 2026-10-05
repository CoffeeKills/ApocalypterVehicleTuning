# Panel string inventory (for translators)

The mod's panel is plain English. ApocaLanguage (nexusmods.com/apocalypter/mods/17) translates
on-screen uGUI text automatically when a translation exists for it, and its in-game
`CollectStrings` tool writes every on-screen English string to `_collected.json` — the most
reliable way to gather the current strings. This file documents what is where and the
formatting rules dynamic strings follow.

## Reproducing the inventory

Every display string lives in `SettingsPanel.cs`, `SettingsPanelManager.cs`, `GearGraph.cs`
and the `Settings/*Preset.cs` files (0.6.0 adds `AlignmentPreset.cs` and `GearboxPreset.cs`,
already covered by the glob), 0.7.0's layout template names in `Settings/DrivetrainSettings.cs`,
plus the templates in `Runtime/UiStrings.cs`:

```bash
grep -o '"[^"]\{3,\}"' plugin/Runtime/SettingsPanel.cs plugin/Runtime/SettingsPanelManager.cs plugin/Runtime/GearGraph.cs plugin/Settings/*Preset.cs plugin/Settings/DrivetrainSettings.cs | sed 's/^[^:]*://' | sort -u
```

(0.7.0 sweep: 508 unique quoted strings across those files, display and internal together.)

Not every hit is a display string: internal object names (`AeroPage`, `ApocalypterSettingsCanvas`,
`ApocalypterSteeringMod_Button`, `Dim`, `Footer`, `Body`, `Description`, `Default*`) and format
specifiers (`"0.00"`) appear in the sweep but are never shown. Display strings break down as:

1. **UI chrome** — tab names (`Steering`, `Suspension`, `Aero`, `Brakes`, `Grip`, `Drivetrain`,
   `Assists`, and since 0.6.0 `Alignment`, `Gearbox`, `Settings` — "Panel" until the 0.6.0 release prep), footer buttons (`Copy preset`,
   `Paste preset`, `Turn everything off`, `Click again`), the transmission-mode labels
   (`Stock`, `Manual`, `Automatic`), the clutch-type labels (`Stock`, `Street`, `Sport`, `Race`,
   `Custom`), the telemetry corner labels (`Top left` …), the gear-count value `Own`, `ON`, `OFF`, `Reset`, `Done`, `Close`, `X`, `Click again to confirm`,
   the diff-mode labels (`Stock`, `Open`, `Locked`, `LSD`), footer hints, preset-button rows.
2. **Per-tab titles, hints, notes** — the static labels passed to `AddSectionTitle` /
   `AddOption` / `AddSlider` / `AddNote` in each `Build*` method (e.g. `"Steering speed"`,
   `"Grip window"`, `"Diff stiffness"`). These are complete sentences or short labels.
3. **Preset names/labels/descriptions** — the `Name`, `Label`, `Description` fields of all
   seven preset classes. See below for the one rule about `Name`.
4. **Dynamic templates** — everything in `Runtime/UiStrings.cs` (see next section).
5. **Menu button** — the cloned pause-menu button is labelled `Vehicle Tuning`, or
   `VEHICLE TUNING` when the template button it copies is all-caps. Section titles are
   displayed uppercased by the panel; ApocaLanguage matches case-insensitively, but a
   translation pack may provide both forms.
6. **Curve editors (0.4.0)** — the two graph rows (`Lock at speed`, `Return to center`)
   receive their title and hint as static literals passed from `SettingsPanel.cs`
   (the sweep command above captures them). Drag feedback ("63 km/h · 40%") and the
   point-count readout are built by concatenation in `CurveEditor.cs` and contain no
   templates; only the fixed `km/h`, `%`, `points` and `·` fragments are translatable
   via exact-match entries.
7. **Gear graph and telemetry (0.6.0)** — the gear-graph row title/hint are static literals
   from `SettingsPanel.cs`. Its readout ("Gear 3 · ratio 1.439") concatenates two templates
   (`GearTitleFmt`, `RatioFmt`) with a fixed ` · `; each part translates on its own. The
   engagement-point value ("-200 rpm") is built as number + the fixed ` rpm` fragment. The
   telemetry strip uses only the four `Telemetry*Fmt` templates. The paste status may append
   `PresetPasteSkippedFmt` after `PresetPastedFmt` with a space.
8. **Alignment slider titles (0.6.0)** are concatenated from fixed parts: `Camber` / `Track
   (outward)` / `Height` / `Fore/aft` + ` front` / ` rear` or ` front left` … ` rear right`.
   Translate the parts as exact-match entries or the whole titles as collected.

9. **0.7.0 additions** — static labels: the per-slider `Pin` button; Gearbox `Shifting`
   section (`Upshift point`, `Downshift point`, `Kickdown`, `Spread gears over the stock range`,
   its note) and the `Truck` preset (Name = Label = `Truck`, plus its Description); Drivetrain
   centre-diff buttons (the same `Stock` / `Open` / `Locked` / `LSD` labels), `Drivetrain layout`
   section (`Custom layout`, the template buttons `RWD`, `FWD`, `AWD`, `4x4 locked`, `6x6`,
   `Copy this vehicle's layout`, `Paste layout`); Settings tab `Pinned values` option.
   **Telemetry pin cells** show two texts: the label (the slider's own title once the panel was
   built — the same string as in the panel, so one translation covers both — else the config key
   in words, e.g. `Spring front`) and the value in the slider's units (the existing `Times`,
   `Deg`, `DegSigned`, `CmSigned`, `Percent`, `SpeedMps`, `GearCountFmt` templates, `Own`, and
   `TelemetryRpmFmt` for the clutch offset). The layout status line concatenates
   `LayoutUnfitFmt` + a space + `LayoutTextFmt`; the torque split joins `AxleFrontFmt` /
   `AxleRearFmt` (or `AxleNFmt` + a space + `PercentFmt`) parts with ` · ` inside `SplitFmt`.

## Dynamic templates (UiStrings.cs)

Every dynamic string is a `string.Format` template using only `{0}` (repeated `{0}` allowed):

| Template | `{0}` is | Notes |
|---|---|---|
| `TimesFmt` `"×{0}"` | a multiplier, 2 decimals | |
| `ForceFmt` `"{0} N"` | newtons, grouped | |
| `RateFmt` `"{0} N·s/m"` | newton-seconds per metre | |
| `LengthFmt` `"{0} cm"` | centimetres, integer | |
| `PercentFmt` `"{0}%"` | percent, integer | |
| `DegFmt` `"{0} deg"` | degrees, 1 decimal | |
| `SpeedFmt` `"{0} m/s"` | metres per second, 1 decimal | |
| `CustomPresetFmt` `"Custom ({0})"` | the preset Label the Custom slot was copied from | |
| `CustomBasedOnDescFmt` | the same Label, twice | "Your tuning, based on {0}…" |
| `CustomPlainDesc` | — | no placeholder |
| `PresetEditNoteFmt` | a built-in preset's Description | the note appended to it |
| `GameSpeedHintFoundFmt` | the game's steering-speed number | "…(currently {0}, 50 = normal)" |
| `GameSpeedHintMissing` | — | no placeholder |
| `AppliedOneFmt` / `AppliedManyFmt` | vehicle count | singular and plural are separate templates; the panel picks by `n == 1` |
| `AppliedOneArbFmt` / `AppliedManyArbFmt` | vehicle count | suspension status (anti-roll bar note) |
| `AppliedOneAeroFmt` / `AppliedManyAeroFmt` | vehicle count | aero status |
| `AppliedOneGripFmt` / `AppliedManyGripFmt` | vehicle count | grip status |
| `AppliedOneGripWarnFmt` / `AppliedManyGripWarnFmt` | vehicle count | grip status + tyre-wear warning |
| `AppliedOneDtFmt` / `AppliedManyDtFmt` | vehicle count | drivetrain status |
| `ActiveOnOneFmt` / `ActiveOnManyFmt` | vehicle count | assists status |
| `FrontAxleFmt` / `RearAxleFmt` | a diff-mode label | drivetrain axle notes |
| `TorqueFmt` `"{0} N·m"` | newton-metres, grouped | brake-torque readout (0.6.0) |
| `DegSignedFmt` `"{0}°"` | signed degrees, 1 or 2 decimals | alignment values (0.6.0) |
| `CmSignedFmt` `"{0} cm"` | signed centimetres, integer | alignment position (0.6.0) |
| `PxFmt` `"{0} px"` | pixels, integer | panel width (0.6.0) |
| `RatioFmt` `"ratio {0}"` | a gear ratio, 3 decimals | gear readouts (0.6.0) |
| `AlignmentStockFmt` `"stock {0}"` | a formatted stock value with its unit | alignment second line (0.6.0) |
| `AppliedOneAlignFmt` / `AppliedManyAlignFmt` | vehicle count | alignment status (0.6.0) |
| `GearTitleFmt` `"Gear {0}"` | gear number | gear slider titles, graph readout (0.6.0) |
| `GearCountFmt` `"{0} gears"` | a gear count | gear-count slider (0.6.0) |
| `AppliedOneGearFmt` / `AppliedManyGearFmt` | vehicle count | gearbox status (0.6.0) |
| `TelemetrySpeedFmt` / `TelemetryRpmFmt` / `TelemetryGearFmt` / `TelemetrySlipFmt` | km/h integer / rpm integer / gear name (`N`, `R1`, `3`) / approximate degrees, 1 decimal | telemetry strip (0.6.0) |
| `PresetCopiedFmt` | the copied preset's label | footer status (0.6.0) |
| `PresetPastedFmt` | number of values applied | footer status (0.6.0) |
| `PresetPasteSkippedFmt` | number of skipped values | appended to the pasted status (0.6.0) |
| `PresetPasteFailedFmt` | one of the `PasteReason*` strings | footer status (0.6.0) |
| `PasteReasonEmpty` / `PasteReasonWrongTag` / `PasteReasonWrongCategory` / `PasteReasonMalformed` / `PasteReasonNoClipboard` | — | no placeholder (0.6.0) |
| `ShiftedOneFmt` / `ShiftedManyFmt` | vehicle count | gearbox status: vehicles the mod shifts (0.7.0) |
| `CenterDiffFmt` | a diff-mode label | drivetrain centre-diff note (0.7.0) |
| `SplitNoVehicle` | — | no placeholder (0.7.0) |
| `SplitOneAxleFmt` | a vehicle name | "… drives one axle …" (0.7.0) |
| `SplitFmt` | the joined per-axle parts | torque split readout (0.7.0) |
| `AxleFrontFmt` / `AxleRearFmt` | percent, integer | "front 40%" / "rear 60%" (0.7.0) |
| `AxleNFmt` | axle number | vehicles with 3+ axles, followed by `PercentFmt` (0.7.0) |
| `LayoutOffFmt` / `LayoutNeedsDrivetrainFmt` / `LayoutActiveFmt` / `LayoutTextFmt` | the layout text (config syntax, never translate it inside a pack) | layout status (0.7.0) |
| `LayoutInvalidFmt` / `LayoutPasteFailedFmt` | the parser's English error message | layout status / footer (0.7.0) |
| `LayoutUnfitFmt` | why the vehicle keeps its own drivetrain (English) | layout status (0.7.0) |
| `LayoutCopied` / `LayoutCopyNone` / `LayoutPasted` | — | no placeholder; footer status (0.7.0) |
| `TargetMissingFmt` | the selected vehicle's name | "… (not here)" while it has not spawned (0.7.0) |
| `PinnedFmt` / `PinnedTelemetryOnFmt` / `UnpinnedFmt` | a slider title | footer status after a pin click (0.7.0) |
| `PinsFullFmt` | the pin cap (12) | footer status (0.7.0) |

**Conventions**

- Only `{0}` is used; never add new placeholder types.
- Units live inside the template so a translation can reposition or localise them.
- Singular and plural are separate templates — do not merge them.
- No angle brackets in any string: panel labels render rich text, and a stray `<`
  would be parsed as a tag and vanish.
- Preset **`Name` is a config ID** and must never be translated or renamed (the config
  file stores it); **`Label` is the display string** and is what translators translate.
  Steering is the only category where they differ (e.g. `Name = "GTA-style Keyboard"`,
  `Label = "GTA-style"`). The labels of the other categories equal their names.

## Fonts

The panel uses Unity's built-in Arial (dynamic). ApocaLanguage handles per-language font
fallbacks; nothing is needed here.
