# Panel string inventory (for translators)

The mod's panel is plain English. ApocaLanguage (nexusmods.com/apocalypter/mods/17) translates
on-screen uGUI text automatically when a translation exists for it, and its in-game
`CollectStrings` tool writes every on-screen English string to `_collected.json` — the most
reliable way to gather the current strings. This file documents what is where and the
formatting rules dynamic strings follow.

## Reproducing the inventory

Every display string lives in `SettingsPanel.cs`, `SettingsPanelManager.cs` and the
`Settings/*Preset.cs` files, plus the templates in `Runtime/UiStrings.cs`:

```bash
grep -o '"[^"]\{3,\}"' plugin/Runtime/SettingsPanel.cs plugin/Runtime/SettingsPanelManager.cs plugin/Settings/*Preset.cs | sed 's/^[^:]*://' | sort -u
```

Not every hit is a display string: internal object names (`AeroPage`, `ApocalypterSettingsCanvas`,
`ApocalypterSteeringMod_Button`, `Dim`, `Footer`, `Body`, `Description`, `Default*`) and format
specifiers (`"0.00"`) appear in the sweep but are never shown. Display strings break down as:

1. **UI chrome** — tab names (`Steering`, `Suspension`, `Aero`, `Brakes`, `Grip`, `Drivetrain`,
   `Assists`), `ON`, `OFF`, `Reset`, `Done`, `Close`, `X`, `Click again to confirm`,
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
