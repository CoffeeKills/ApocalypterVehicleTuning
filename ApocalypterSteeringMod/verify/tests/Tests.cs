using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using NWH.WheelController3D;
using ApocalypterSteeringMod.Persistence;
using ApocalypterSteeringMod.Runtime;
using ApocalypterSteeringMod.Game;
using ApocalypterSteeringMod.Settings;
using BepInEx.Configuration;
using NWH.Common.Vehicles;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Modules;
using NWH.VehiclePhysics2.Modules.Aerodynamics;
using NWH.VehiclePhysics2.Powertrain;
using NWH.VehiclePhysics2.Powertrain.Wheel;
using UnityEngine;

public sealed class FakeWheel : WheelUAPI
{
    public override float SteerAngle { get; set; }
    public override float SpringMaxLength { get; set; }
    public override float SpringMaxForce { get; set; }
    public override float DamperBumpRate { get; set; }
    public override float DamperReboundRate { get; set; }
    public override float LongitudinalFrictionGrip { get; set; }
    public override float LateralFrictionGrip { get; set; }
    public override float LongitudinalFrictionStiffness { get; set; }
    public override float LateralFrictionStiffness { get; set; }
    private bool _grounded = true;
    private float _longitudinalSlip;
    public override bool IsGrounded { get { return _grounded; } }
    public override float LongitudinalSlip { get { return _longitudinalSlip; } }
    public void SetGrounded(bool v) { _grounded = v; }
    public void SetLongitudinalSlip(float v) { _longitudinalSlip = v; }
}

/// <summary>0.6.3: a module manager that throws like a half-initialised one mid-spawn.</summary>
public sealed class ThrowingModuleManager : ModuleManager
{
    public bool Throw = true;
    public override List<NWH.VehiclePhysics2.VehicleComponent> Components
    {
        get { if (Throw) throw new InvalidOperationException("half-initialised (test)"); return base.Components; }
    }
}

/// <summary>0.6.3: a wheel whose spring setter throws (apply-time fault on one vehicle).</summary>
public sealed class ThrowingWheel : WheelUAPI
{
    public bool Throw;
    private float _spring = 30000f;
    public override float SteerAngle { get; set; }
    public override float SpringMaxLength { get; set; }
    public override float SpringMaxForce
    {
        get { return _spring; }
        set { if (Throw) throw new InvalidOperationException("wheel fault (test)"); _spring = value; }
    }
    public override float DamperBumpRate { get; set; }
    public override float DamperReboundRate { get; set; }
    public override float LongitudinalFrictionGrip { get; set; }
    public override float LateralFrictionGrip { get; set; }
    public override float LongitudinalFrictionStiffness { get; set; }
    public override float LateralFrictionStiffness { get; set; }
    public override bool IsGrounded { get { return true; } }
    public override float LongitudinalSlip { get { return 0f; } }
}

public static class Tests
{
    private static int _fail, _pass;

    private static void Check(bool ok, string what)
    {
        if (ok) { _pass++; Console.WriteLine("  ok   " + what); }
        else { _fail++; Console.WriteLine("  FAIL " + what); }
    }

    private static bool Near(float a, float b, float eps = 1e-3f) { return Math.Abs(a - b) <= eps; }

    // The tuner's weight capture (0.10.0) onboards a WeightLiftModule into every
    // captured vehicle, so module-list checks must filter by type.
    private static int CountOf<T>(VehicleController vc) where T : NWH.VehiclePhysics2.VehicleComponent
    {
        int n = 0;
        foreach (NWH.VehiclePhysics2.VehicleComponent c in vc.moduleManager.Components)
        {
            if (c is T) n++;
        }
        return n;
    }

    private static T FindOf<T>(VehicleController vc) where T : NWH.VehiclePhysics2.VehicleComponent
    {
        foreach (NWH.VehiclePhysics2.VehicleComponent c in vc.moduleManager.Components)
        {
            if (c is T) return (T)c;
        }
        return null;
    }

    private static VehicleController MakeCar(out FakeWheel[] wheels, float pivotZ)
    {
        var vc = new VehicleController();
        vc.vehicleRigidbody = new Rigidbody();
        vc.wheelbase = 2.6f;
        vc.fixedDeltaTime = 0.02f;
        vc.Speed = 10f;
        vc.LocalForwardVelocity = 10f;
        vc.moduleManager.vehicleController = vc;
        wheels = new FakeWheel[4];
        // Two axles; the vehicle pivot sits at pivotZ (e.g. on the rear axle, which used to break front/rear detection).
        float[] z = { 1.3f, 1.3f, -1.3f, -1.3f };
        for (int g = 0; g < 2; g++)
        {
            var group = new WheelGroup
            {
                antiRollBarForce = g == 0 ? 5000f : 0f,
                addAckerman = false,
                steerCoefficient = g == 0 ? 1f : 0f,
                brakeCoefficient = 1f,
                handbrakeCoefficient = 1f
            };
            for (int i = 0; i < 2; i++)
            {
                var w = new FakeWheel
                {
                    SpringMaxForce = 30000f,
                    SpringMaxLength = 0.3f,
                    DamperBumpRate = 3000f,
                    DamperReboundRate = 3500f,
                    LongitudinalFrictionGrip = 1f,
                    LateralFrictionGrip = 1f,
                    LongitudinalFrictionStiffness = 1f,
                    LateralFrictionStiffness = 1f
                };
                w.transform.position = new Vector3(0f, 0f, z[g * 2 + i] - pivotZ);
                wheels[g * 2 + i] = w;
                var wc = new WheelComponent { wheelUAPI = w };
                group.Wheels.Add(wc);
                vc.powertrain.wheels.Add(wc);
            }
            vc.powertrain.wheelGroups.Add(group);
        }
        for (int d = 0; d < 2; d++)
        {
            vc.powertrain.differentials.Add(new DifferentialComponent { DifferentialType = DifferentialComponent.Type.Open, biasAB = 0.5f, stiffness = 0.5f });
        }
        return vc;
    }

    public static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "asm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        Console.WriteLine("Config defaults (fresh file, v3.2)");
        string cfgPath = Path.Combine(dir, "mod.cfg");
        ModConfig.Load(new ConfigFile(cfgPath, true));
        Check(!SteeringSettings.Enabled, "steering is opt-in (Enabled = false by default)");
        Check(SteeringSettings.ActivePreset == SteeringPreset.Custom, "steering preset defaults to Custom");
        Check(!SuspensionSettings.Enabled && SuspensionSettings.ActivePreset == SuspensionPreset.Stock, "suspension off, Stock");
        Check(!AeroSettings.Enabled && !BrakesSettings.Enabled && !GripSettings.Enabled && !DrivetrainSettings.Enabled && !AssistsSettings.Enabled,
            "new categories all opt-in (off by default)");
        SteeringPreset c0 = SteeringPreset.Custom;
        Check(Near(c0.RateMultiplier, 1f) && Near(c0.SlipAngleDeg, 8.5f) && Near(c0.OppositeLockBoost, 1.75f) && c0.UseVehicleCurve && !c0.LinearityOverride
              && c0.ReturnCurve.Serialize() == "0:1;1:1",
            "steering Custom defaults == v2.0.0 behaviour (vehicle curve + symmetric return)");

        Console.WriteLine("Steering preset semantics (preset-book)");
        SteeringPreset drift = SteeringPreset.FindBuiltIn("Drift");
        float driftRate = drift.RateMultiplier;
        SteeringSettings.Select(drift);
        SteeringPreset edit = SteeringSettings.BeginEdit();
        edit.RateMultiplier = 2.5f;
        Check(SteeringSettings.ActivePreset == SteeringPreset.Custom, "editing Drift switches to Custom");
        Check(SteeringPreset.Custom.BasedOn == "Drift", "Custom.BasedOn = Drift");
        Check(Near(drift.RateMultiplier, driftRate), "Drift itself untouched");
        Check(SteeringSettings.Reference() == drift, "slider Reset target = Drift");
        SteeringSettings.Select(SteeringPreset.Vanilla);
        Check(SteeringSettings.BeginEdit() == null && SteeringSettings.ActivePreset == SteeringPreset.Vanilla, "Vanilla cannot be edited");
        SteeringSettings.Select(SteeringPreset.Custom);

        Console.WriteLine("Suspension preset semantics (sliders show preset factors)");
        SuspensionPreset race = SuspensionSettings.Book.FindBuiltIn("Race");
        SuspensionSettings.SetPresetByName("Race");
        Check(SuspensionSettings.Shown == race, "Shown = Race");
        Check(Near(SuspensionSettings.Spring(true), race.SpringFront) && Near(SuspensionSettings.Spring(false), race.SpringRear), "factors come straight from the preset");
        SuspensionPreset sEdit = SuspensionSettings.BeginEdit();
        sEdit.SpringRear = 0.5f;
        Check(SuspensionSettings.ActivePreset == SuspensionPreset.Custom && SuspensionPreset.Custom.BasedOn == "Race", "editing Race copies to Custom(Race)");
        Check(Near(race.SpringRear, race.SpringRear) && Near(SuspensionSettings.Spring(false), 0.5f) && Near(SuspensionSettings.Spring(true), race.SpringFront),
            "Race untouched; rear factor edited, front carried over");
        Check(SuspensionSettings.Reference() == race, "suspension slider Reset target = Race");
        SuspensionSettings.SetPresetByName("Stock");
        SuspensionSettings.BeginEdit();
        Check(SuspensionPreset.Custom.BasedOn == "", "editing Stock copies with BasedOn = \"\"");
        SuspensionSettings.ResetAll();

        Console.WriteLine("Config save/reload round-trip");
        SuspensionSettings.Enabled = true;
        SuspensionSettings.SetPresetByName("Sport");
        SuspensionSettings.BeginEdit().SpringFront = 1.5f;
        DrivetrainSettings.Enabled = true;
        DrivetrainSettings.BeginEdit().PowerScale = 2f;
        ModConfig.Save();
        SuspensionSettings.ResetAll();
        DrivetrainSettings.ResetAll();
        ModConfig.Load(new ConfigFile(cfgPath, true));
        Check(SuspensionSettings.Enabled && SuspensionSettings.ActivePreset == SuspensionPreset.Custom
              && SuspensionPreset.Custom.BasedOn == "Sport" && Near(SuspensionPreset.Custom.SpringFront, 1.5f),
            "suspension Custom persisted (BasedOn + factor)");
        Check(DrivetrainSettings.Enabled && DrivetrainSettings.ActivePreset == DrivetrainPreset.Custom
              && Near(DrivetrainPreset.Custom.PowerScale, 2f), "drivetrain Custom persisted");

        Console.WriteLine("v3.1 -> v3.2 migration");
        TestMigration(dir);

        Console.WriteLine("Out-of-range and legacy config values");
        string legacy = Path.Combine(dir, "legacy.cfg");
        File.WriteAllText(legacy, "[Steering.Custom]\nRateMultiplier = 99\nSlipAngleDeg = -4\n\n[Suspension]\nPreset = Street\n\n[Drivetrain.Custom]\nPowerScale = 99\nDiffFrontMode = Warp\n");
        ModConfig.Load(new ConfigFile(legacy, true));
        Check(Near(SteeringPreset.Custom.RateMultiplier, Limits.RateMax), "steering RateMultiplier 99 clamped to " + Limits.RateMax);
        Check(Near(SteeringPreset.Custom.SlipAngleDeg, Limits.SlipMin), "steering SlipAngleDeg -4 clamped to " + Limits.SlipMin);
        Check(SuspensionSettings.ActivePreset == SuspensionPreset.Stock, "v3.0 preset 'Street' maps to Stock");
        Check(Near(DrivetrainPreset.Custom.PowerScale, Limits.PowerMax), "drivetrain PowerScale 99 clamped to " + Limits.PowerMax);
        Check(DrivetrainPreset.Custom.DiffFrontMode == DiffMode.Stock, "DiffFrontMode 'Warp' falls back to Stock");

        Console.WriteLine("VehicleTuner: suspension, grip, brakes, drivetrain");
        TestSuspensionAndFriends();

        Console.WriteLine("VehicleTuner: brakes balance (0.2.0)");
        TestBrakeBalance();

        Console.WriteLine("VehicleTuner: drivetrain diffs + shift points (0.2.0)");
        TestDrivetrain();

        Console.WriteLine("VehicleTuner: aero (module + onboarding)");
        TestAero();

        Console.WriteLine("VehicleTuner: aero stock fidelity (0.2.0)");
        TestAeroFidelity();

        Console.WriteLine("VehicleTuner: assists (ABS/TCS delegates)");
        TestAssists();

        Console.WriteLine("VehicleTuner: lifecycle (0.2.0)");
        TestLifecycle();

        Console.WriteLine("Config hardening (0.2.0)");
        TestConfigHardening(dir);

        Console.WriteLine("0.3.0 regressions (Euro Truck, Apocasetter key, UiStrings)");
        TestEuroTruck(dir);
        TestLegacySteeringMigration(dir);
        TestApocasetterKey(dir);
        TestUiStrings();

        Console.WriteLine("0.4.0 (editable curves + steering curve migration)");
        TestEditableCurve();
        TestSteeringCurveMigration(dir);

        Console.WriteLine("0.5.0 regressions");
        TestCurveRoundTrip();
        TestAeroOnboardingDrag();
        TestBaselineRefresh();
        TestCurveEditorLayout();
        TestCurveEditorRouting();

        Console.WriteLine("0.6.0 audit regressions");
        TestAudit060(dir);

        Console.WriteLine("0.6.0 Alignment");
        TestAlignment();

        Console.WriteLine("0.6.0 Gearbox");
        TestGearbox();

        Console.WriteLine("0.6.1 targeting (apply-to selection)");
        TestTargeting();

        Console.WriteLine("0.6.0 input blocker routing");
        TestInputBlocker();

        Console.WriteLine("0.6.0 preset codec");
        TestPresetCodec();

        Console.WriteLine("0.6.0 config (defaults, 0.5.0 file, ranges)");
        TestConfig060(dir);

        Console.WriteLine("0.6.0 panel layout");
        TestPanelLayout();

        Console.WriteLine("0.6.0 telemetry + small extras");
        TestTelemetryAndExtras();

        Console.WriteLine("0.6.2 drivetrain layout: parser + validator");
        TestLayoutParser();

        Console.WriteLine("0.6.2 drivetrain layout: wiring, stepping, restore");
        TestLayoutRuntime();

        Console.WriteLine("0.6.2 drivetrain layout: config");
        TestLayoutConfig(dir);

        Console.WriteLine("0.6.2 curve editor: drag picks at the press position");
        TestCurveEditorPick();

        Console.WriteLine("0.6.2 apply path allocation");
        TestApplyAllocation();

        Console.WriteLine("0.6.3 driven-vehicle pick (telemetry + Last driven)");
        TestDrivenPick();

        Console.WriteLine("0.6.3 crash hardening: per-category guards");
        TestFaultGuards();

        // Last: moves the static post-load quiet window (reset at its end).
        Console.WriteLine("0.6.3 crash hardening: spawn-wave scan gate");
        TestSpawnGate();

        Console.WriteLine("0.6.4 parked-car frozen input (telemetry pick)");
        TestFrozenInputPick();

        Console.WriteLine("0.8.0 max steer angle + save tracking");
        TestSaveAndSteerExtras(dir);

        Console.WriteLine("0.7.0 shift controller: pure shift-point math");
        TestShiftMath();

        Console.WriteLine("0.7.0 shift controller: hook, shifting, restore");
        TestShiftController();

        Console.WriteLine("0.7.0 Truck gearbox preset");
        TestTruckPreset();

        Console.WriteLine("0.7.0 game-changed values (drift) + centre diff + torque split");
        TestDriftAndCentre();

        Console.WriteLine("0.7.0 telemetry pins");
        TestTelemetryCells(dir);

        Console.WriteLine("0.7.0 tighter panel layout (300 / 400 / 460 / 800 / 1000 px)");
        TestLayout070();

        Console.WriteLine("0.7.0 config: a real 0.6.4 file + new keys");
        TestConfig070(dir);

        Console.WriteLine("0.9.0 per-vehicle tunes: book semantics + blob round-trip");
        TestPerVehicleTunes(dir);

        Console.WriteLine("0.7.0 audit fixes (UI)");
        TestAudit070();

        Console.WriteLine();
        Console.WriteLine(_pass + " passed, " + _fail + " failed");
        return _fail == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- migration

    private static void TestMigration(string dir)
    {
        // 1. Fold: legacy user multipliers merge into Custom.
        string path = Path.Combine(dir, "mig1.cfg");
        File.WriteAllText(path, "[Suspension]\nPreset = Sport\n\n[Suspension.User]\nSpringFront = 1.5\nRideHeightRear = 0.8\n");
        ModConfig.Load(new ConfigFile(path, true));
        SuspensionPreset sport = SuspensionSettings.Book.FindBuiltIn("Sport");
        Check(SuspensionSettings.ActivePreset == SuspensionPreset.Custom, "fold: active becomes Custom");
        Check(SuspensionPreset.Custom.BasedOn == "Sport", "fold: BasedOn = Sport");
        Check(Near(SuspensionPreset.Custom.SpringFront, sport.SpringFront * 1.5f), "fold: SpringFront = Sport x 1.5 (" + SuspensionPreset.Custom.SpringFront + ")");
        Check(Near(SuspensionPreset.Custom.RideHeightRear, sport.RideHeightRear * 0.8f), "fold: RideHeightRear = Sport x 0.8");
        Check(Near(SuspensionPreset.Custom.SpringRear, sport.SpringRear), "fold: untouched factor = Sport factor");
        Check(SuspensionSettings.Reference() == sport, "fold: Reset target = Sport");

        // 2. Save + reload: no double fold, values identical, legacy section gone.
        ModConfig.Save();
        ModConfig.Load(new ConfigFile(path, true));
        Check(SuspensionSettings.ActivePreset == SuspensionPreset.Custom && SuspensionPreset.Custom.BasedOn == "Sport"
              && Near(SuspensionPreset.Custom.SpringFront, sport.SpringFront * 1.5f), "fold survives save/reload unchanged");
        string txt = File.ReadAllText(path);
        Check(!txt.Contains("[Suspension.User]"), "legacy [Suspension.User] section removed from file");

        // 3. All user values 1.0: no fold, preset stays.
        string path2 = Path.Combine(dir, "mig2.cfg");
        File.WriteAllText(path2, "[Suspension]\nPreset = Sport\n\n[Suspension.User]\nSpringFront = 1.0\n");
        ModConfig.Load(new ConfigFile(path2, true));
        Check(SuspensionSettings.ActivePreset == sport && SuspensionPreset.Custom.BasedOn == "", "all-1.0 users: preset stays, no fold");

        // 4. Stock + user values: fold into Custom with BasedOn "".
        string path3 = Path.Combine(dir, "mig3.cfg");
        File.WriteAllText(path3, "[Suspension]\nPreset = Stock\n\n[Suspension.User]\nArbRear = 1.4\n");
        ModConfig.Load(new ConfigFile(path3, true));
        Check(SuspensionSettings.ActivePreset == SuspensionPreset.Custom
              && SuspensionPreset.Custom.BasedOn == ""
              && Near(SuspensionPreset.Custom.ArbRear, 1.4f), "Stock + user values fold with BasedOn \"\"");
        SuspensionSettings.ResetAll();
    }

    // ---------------------------------------------------------------- tuner systems

    private static void TestSuspensionAndFriends()
    {
        UnityEngine.Object.Registry.Clear();
        // Pivot on the rear axle: every wheel has z >= 0 relative to the pivot.
        VehicleController vc = MakeCar(out FakeWheel[] w, -1.3f);
        vc.powertrain.wheelGroups[1].brakeCoefficient = 0.5f;   // part of the stock setup from here on
        UnityEngine.Object.Registry.Add(vc);

        var tuner = new VehicleTuner();
        SuspensionSettings.ResetAll();
        BrakesSettings.ResetAll();
        GripSettings.ResetAll();
        DrivetrainSettings.ResetAll();

        // Suspension: preset factor is the value.
        SuspensionSettings.Enabled = true;
        SuspensionSettings.SetPresetByName("Race");
        SuspensionPreset race = SuspensionSettings.ActivePreset;
        tuner.ReapplyNow();
        Check(tuner.TrackedVehicles == 1, "vehicle tracked");
        Check(Near(w[0].SpringMaxForce, 30000f * race.SpringFront), "front spring = stock x Race front (" + w[0].SpringMaxForce + ")");
        Check(Near(w[2].SpringMaxForce, 30000f * race.SpringRear), "rear spring = stock x Race rear");
        Check(Near(w[0].SpringMaxLength, 0.3f * race.RideHeightFront), "front ride height scaled");
        Check(Near(vc.powertrain.wheelGroups[0].antiRollBarForce, 5000f * race.ArbFront), "front ARB scaled");
        Check(vc.powertrain.wheelGroups[1].antiRollBarForce == 0f, "rear ARB stays 0 (car had none)");
        Check(Near(tuner.MeanBaseline(VehicleTuner.Readout.SpringForce, true), 30000f), "MeanBaseline readout = stock value");

        // Copy-to-Custom semantics end-to-end.
        SuspensionPreset custom = SuspensionSettings.BeginEdit();
        custom.SpringRear = 0.5f;
        tuner.ApplyLive();
        Check(Near(w[0].SpringMaxForce, 30000f * race.SpringFront), "front keeps Race factor after edit");
        Check(Near(w[2].SpringMaxForce, 30000f * 0.5f), "rear = stock x edited Custom factor");
        tuner.ApplyLive();
        Check(Near(w[2].SpringMaxForce, 30000f * 0.5f), "re-apply does not compound");

        // Grip.
        GripSettings.Enabled = true;
        GripSettings.SetPresetByName("Drift");
        GripPreset drift = GripSettings.ActivePreset;
        tuner.ApplyLive();
        Check(Near(w[0].LateralFrictionGrip, 1f * drift.LateralScale), "lateral grip scaled per wheel");
        Check(Near(w[0].LongitudinalFrictionStiffness, 1f * drift.StiffnessScale), "stiffness scaled per wheel");

        // Brakes: effective axle torque (maxTorque x coefficient) = stock x Torque x axle factor,
        // even where the factor pushes a 1.0 coefficient past the game's 0..1 range.
        BrakesSettings.Enabled = true;
        BrakesSettings.SetPresetByName("Sport");
        BrakesPreset sport = BrakesSettings.ActivePreset;
        tuner.ApplyLive();
        float mt = vc.brakes.maxTorque;
        Check(Near(mt * vc.powertrain.wheelGroups[0].brakeCoefficient, 7000f * sport.TorqueScale * 1f * sport.FrontBrakeScale, 0.5f),
            "front axle torque = stock x Torque x Front (1.0 x 1.1 is no longer clamped away)");
        Check(Near(mt * vc.powertrain.wheelGroups[1].brakeCoefficient, 7000f * sport.TorqueScale * 0.5f * sport.RearBrakeScale, 0.5f),
            "rear axle torque = stock x Torque x Rear (brake balance kept)");
        Check(Near(mt * vc.powertrain.wheelGroups[1].handbrakeCoefficient, 7000f * sport.TorqueScale * 1f * sport.HandbrakeScale, 0.5f),
            "handbrake torque = stock x Torque x Handbrake");
        Check(vc.powertrain.wheelGroups[0].brakeCoefficient <= 1f && vc.powertrain.wheelGroups[1].handbrakeCoefficient <= 2f,
            "coefficients stay inside the game's ranges");

        // Drivetrain incl. differential modes.
        DrivetrainSettings.Enabled = true;
        DrivetrainSettings.SetPresetByName("Race");
        DrivetrainPreset dRace = DrivetrainSettings.ActivePreset;
        tuner.ApplyLive();
        Check(Near(vc.powertrain.engine.maxPower, 120f * dRace.PowerScale), "engine power scaled");
        Check(Near(vc.powertrain.engine.revLimiterRPM, 4700f * dRace.RevLimiterScale), "rev limiter scaled");
        Check(Near(vc.powertrain.transmission.finalGearRatio, 6f * dRace.FinalDriveScale), "final drive scaled");
        Check(vc.powertrain.differentials[0].DifferentialType == DifferentialComponent.Type.Locked, "front diff locked (Race)");
        Check(vc.powertrain.differentials[1].DifferentialType == DifferentialComponent.Type.Locked, "rear diff locked (Race)");

        // A vehicle with no differentials must survive apply/restore.
        var vcNoDiff = MakeCar(out FakeWheel[] w2, 0f);
        vcNoDiff.powertrain.differentials.Clear();
        UnityEngine.Object.Registry.Add(vcNoDiff);
        tuner.ReapplyNow();   // captures the second vehicle with an empty diff list
        Check(tuner.TrackedVehicles == 2, "second vehicle tracked");
        Check(Near(vcNoDiff.powertrain.engine.maxPower, 120f * dRace.PowerScale), "no-diff vehicle still tuned");
        DrivetrainSettings.BeginEdit().DiffFrontMode = DiffMode.LimitedSlip;
        tuner.ApplyLive();
        Check(vc.powertrain.differentials[0].DifferentialType == DifferentialComponent.Type.LimitedSlip, "diff mode edit applies");

        // Restore everything.
        SuspensionSettings.Enabled = false;
        GripSettings.Enabled = false;
        BrakesSettings.Enabled = false;
        DrivetrainSettings.Enabled = false;
        tuner.ApplyLive();
        Check(Near(w[0].SpringMaxForce, 30000f) && Near(w[2].SpringMaxForce, 30000f) && Near(w[3].DamperReboundRate, 3500f),
            "disable restores suspension stock values");
        Check(Near(vc.powertrain.wheelGroups[0].antiRollBarForce, 5000f), "disable restores ARB");
        Check(Near(w[0].LateralFrictionGrip, 1f) && Near(w[0].LongitudinalFrictionStiffness, 1f), "disable restores grip");
        Check(Near(vc.brakes.maxTorque, 7000f), "disable restores brake torque");
        Check(Near(vc.powertrain.wheelGroups[1].brakeCoefficient, 0.5f), "disable restores rear brakeCoefficient");
        Check(Near(vc.powertrain.engine.maxPower, 120f) && Near(vc.powertrain.engine.revLimiterRPM, 4700f)
              && Near(vc.powertrain.transmission.finalGearRatio, 6f), "disable restores drivetrain");
        Check(vc.powertrain.differentials[0].DifferentialType == DifferentialComponent.Type.Open
              && vc.powertrain.differentials[1].DifferentialType == DifferentialComponent.Type.Open, "disable restores diff types");
        SuspensionSettings.ResetAll();
        BrakesSettings.ResetAll();
        GripSettings.ResetAll();
        DrivetrainSettings.ResetAll();
    }

    // ---------------------------------------------------------------- aero

    private static void TestAero()
    {
        UnityEngine.Object.Registry.Clear();
        var tuner = new VehicleTuner();
        AeroSettings.ResetAll();

        // (a) Vehicle WITH a module: fields scaled, no onboarding.
        var vcWith = MakeCar(out FakeWheel[] _, 0f);
        vcWith.moduleManager.Components.Add(new AerodynamicsModule
        {
            frontalCd = 0.4f,
            sideCd = 1.0f,
            maxDownforceSpeed = 60f,
            downforcePoints = { new DownforcePoint { maxForce = 5000f, position = Vector3.zero } }
        });
        UnityEngine.Object.Registry.Add(vcWith);

        AeroSettings.Enabled = true;
        AeroSettings.SetPresetByName("Race");
        AeroPreset race = AeroSettings.ActivePreset;
        tuner.ReapplyNow();
        AerodynamicsModule with = FindOf<AerodynamicsModule>(vcWith);
        Check(Near(with.frontalCd, 0.4f * race.DragScale), "module Cd scaled");
        Check(Near(with.downforcePoints[0].maxForce, 5000f * race.DownforceScale), "downforce point scaled");
        Check(CountOf<AerodynamicsModule>(vcWith) == 1, "no extra module onboarded");

        // (b) Vehicle WITHOUT a module: onboarded on apply, disabled on restore, reused.
        var vcWithout = MakeCar(out FakeWheel[] _, 0f);
        UnityEngine.Object.Registry.Add(vcWithout);
        tuner.ReapplyNow();
        Check(CountOf<AerodynamicsModule>(vcWithout) == 1, "module onboarded for vehicle without one");
        AerodynamicsModule added = FindOf<AerodynamicsModule>(vcWithout);
        Check(added.state.isEnabled, "onboarded module enabled");
        Check(Near(added.frontalCd, 0.35f * (race.DragScale - 1f)), "onboarded module Cd = default x the preset's EXCESS drag (0.5.0)");

        AeroSettings.Enabled = false;
        tuner.ApplyLive();
        Check(!added.state.isEnabled, "onboarded module disabled on restore");
        Check(Near(with.frontalCd, 0.4f) && Near(with.downforcePoints[0].maxForce, 5000f), "pre-existing module restored");

        AeroSettings.Enabled = true;
        tuner.ApplyLive();
        Check(CountOf<AerodynamicsModule>(vcWithout) == 1, "re-enable reuses the onboarded module (no duplicates)");
        Check(added.state.isEnabled, "re-enable re-enables the same module");
        AeroSettings.ResetAll();
        AeroSettings.Enabled = false;
        tuner.ApplyLive();
    }

    // ---------------------------------------------------------------- assists

    private static void TestAssists()
    {
        UnityEngine.Object.Registry.Clear();
        VehicleController vc = MakeCar(out FakeWheel[] w, 0f);
        UnityEngine.Object.Registry.Add(vc);
        var tuner = new VehicleTuner();
        AssistsSettings.ResetAll();
        tuner.ReapplyNow();   // capture (no delegates yet)

        AssistsSettings.Enabled = true;
        AssistsSettings.SetPresetByName("Standard");
        tuner.ApplyLive();
        Check(vc.brakes.brakeTorqueModifiers.Count == 1, "ABS delegate registered once");
        Check(vc.powertrain.engine.powerModifiers.Count == 1, "TCS delegate registered once");
        tuner.ApplyLive();
        tuner.ApplyLive();
        Check(vc.brakes.brakeTorqueModifiers.Count == 1 && vc.powertrain.engine.powerModifiers.Count == 1,
            "repeated apply does not double-register");

        // Delegate behavior: slipping wheel -> cut; below threshold -> full.
        // NWH convention: braking = positive longitudinal slip (ABS), spinning = negative (TCS).
        w[0].SetGrounded(true);
        w[0].SetLongitudinalSlip(0.3f);
        Check(Near(vc.brakes.brakeTorqueModifiers[0](), 0.01f), "ABS cuts while a wheel slips");
        w[0].SetLongitudinalSlip(-0.3f);
        Check(Near(vc.powertrain.engine.powerModifiers[0](), 0.01f), "TCS cuts while a wheel spins");
        w[0].SetLongitudinalSlip(0.05f);
        Check(Near(vc.brakes.brakeTorqueModifiers[0](), 1f), "ABS passes below threshold");
        Check(Near(vc.powertrain.engine.powerModifiers[0](), 1f), "TCS passes below threshold");

        // Handbrake pulled: a locked rear wheel must NOT trip ABS (the modifier also
        // scales the handbrake torque) — mirrors ABSModule's Handbrake < 0.1 guard.
        w[2].SetLongitudinalSlip(0.6f);
        vc.input.Handbrake = 1f;
        Check(Near(vc.brakes.brakeTorqueModifiers[0](), 1f), "ABS stands down while the handbrake is pulled");
        vc.input.Handbrake = 0.05f;
        Check(Near(vc.brakes.brakeTorqueModifiers[0](), 0.01f), "ABS works again once the handbrake is released");
        vc.input.Handbrake = 0f;
        w[2].SetLongitudinalSlip(0f);

        AssistsSettings.Enabled = false;
        tuner.ApplyLive();
        Check(vc.brakes.brakeTorqueModifiers.Count == 0 && vc.powertrain.engine.powerModifiers.Count == 0,
            "disable removes both delegates");

        AssistsSettings.Enabled = true;
        tuner.ApplyLive();
        Check(vc.brakes.brakeTorqueModifiers.Count == 1 && vc.powertrain.engine.powerModifiers.Count == 1,
            "re-enable re-registers");
        AssistsSettings.ResetAll();
        tuner.ApplyLive();
    }
    // ---------------------------------------------------------------- 0.2.0 regressions

    private static void Invoke(object target, string method)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }

    private static void TestBrakeBalance()
    {
        UnityEngine.Object.Registry.Clear();
        VehicleController vc = MakeCar(out FakeWheel[] _, 0f);
        UnityEngine.Object.Registry.Add(vc);
        var tuner = new VehicleTuner();
        BrakesSettings.ResetAll();
        tuner.ReapplyNow();

        BrakesSettings.Enabled = true;
        BrakesSettings.SetPresetByName("Stock");
        tuner.ApplyLive();
        Check(Near(vc.brakes.maxTorque, 7000f) && Near(vc.powertrain.wheelGroups[0].brakeCoefficient, 1f)
              && Near(vc.powertrain.wheelGroups[1].handbrakeCoefficient, 1f), "Stock preset = shipped values exactly (k = 1)");

        BrakesSettings.SetPresetByName("Drift");
        BrakesPreset drift = BrakesSettings.ActivePreset;
        tuner.ApplyLive();
        float mt = vc.brakes.maxTorque;
        Check(Near(mt * vc.powertrain.wheelGroups[0].brakeCoefficient, 7000f * 0.95f, 0.5f), "Drift: pedal torque = stock x 0.95");
        Check(Near(mt * vc.powertrain.wheelGroups[1].handbrakeCoefficient, 7000f * drift.HandbrakeScale, 0.5f),
            "Drift: full handbrake really 1.6x stronger (was capped at maxTorque)");
        Check(mt >= 7000f * drift.HandbrakeScale - 0.5f, "per-wheel cap (maxTorque) raised to the requested peak");

        BrakesSettings.Enabled = false;
        tuner.ApplyLive();
        Check(Near(vc.brakes.maxTorque, 7000f) && Near(vc.powertrain.wheelGroups[0].brakeCoefficient, 1f)
              && Near(vc.powertrain.wheelGroups[1].handbrakeCoefficient, 1f), "disable restores brakes exactly");
        BrakesSettings.ResetAll();
    }

    private static void TestDrivetrain()
    {
        UnityEngine.Object.Registry.Clear();
        DrivetrainSettings.ResetAll();

        // (a) RWD car with ONE differential feeding the rear wheels. Index mapping used to
        // treat differentials[0] as the front axle.
        VehicleController rwd = MakeCar(out FakeWheel[] _, 0f);
        rwd.powertrain.differentials.Clear();
        var rearDiff = new DifferentialComponent { DifferentialType = DifferentialComponent.Type.Open, biasAB = 0.5f, stiffness = 0.5f };
        rearDiff.OutputB = rwd.powertrain.wheelGroups[1].Wheels[1];
        rwd.powertrain.differentials.Add(rearDiff);
        UnityEngine.Object.Registry.Add(rwd);

        // (b) AWD car: front + rear axle diffs wired to wheels, centre diff wired to a diff,
        // plus an External (script-driven) diff that must never be touched.
        VehicleController awd = MakeCar(out FakeWheel[] _, 0f);
        awd.powertrain.differentials.Clear();
        var fDiff = new DifferentialComponent { DifferentialType = DifferentialComponent.Type.Open, biasAB = 0.5f, stiffness = 0.5f };
        var rDiff = new DifferentialComponent { DifferentialType = DifferentialComponent.Type.Open, biasAB = 0.5f, stiffness = 0.8f };
        var cDiff = new DifferentialComponent { DifferentialType = DifferentialComponent.Type.LimitedSlip, biasAB = 0.4f, stiffness = 0.5f };
        var xDiff = new DifferentialComponent { DifferentialType = DifferentialComponent.Type.External, biasAB = 0.5f, stiffness = 0.5f };
        fDiff.OutputB = awd.powertrain.wheelGroups[0].Wheels[1];
        rDiff.OutputB = awd.powertrain.wheelGroups[1].Wheels[1];
        cDiff.OutputB = rDiff;
        awd.powertrain.differentials.Add(fDiff);
        awd.powertrain.differentials.Add(rDiff);
        awd.powertrain.differentials.Add(cDiff);
        awd.powertrain.differentials.Add(xDiff);
        UnityEngine.Object.Registry.Add(awd);

        int xAssignedAtBuild = xDiff.TypeAssignments;   // the object initializer counts once
        var tuner = new VehicleTuner();
        tuner.ReapplyNow();
        DrivetrainSettings.Enabled = true;
        DrivetrainSettings.SetPresetByName("Drift");   // front Open, rear Locked
        tuner.ApplyLive();
        Check(rearDiff.DifferentialType == DifferentialComponent.Type.Locked, "RWD single diff gets the REAR mode (Locked), not the front one");
        Check(fDiff.DifferentialType == DifferentialComponent.Type.Open && rDiff.DifferentialType == DifferentialComponent.Type.Locked,
            "AWD axle diffs resolved from their wheels");
        Check(cDiff.DifferentialType == DifferentialComponent.Type.LimitedSlip, "centre diff keeps its stock type");
        Check(xDiff.DifferentialType == DifferentialComponent.Type.External && xDiff.TypeAssignments == xAssignedAtBuild, "External diff never assigned");

        // Bias: centre only. Stiffness: capped at NWH's 1.0.
        DrivetrainPreset c = DrivetrainSettings.BeginEdit();
        c.DiffBiasScale = 1.5f;
        c.DiffStiffnessScale = 2f;
        tuner.ApplyLive();
        Check(Near(fDiff.biasAB, 0.5f) && Near(rDiff.biasAB, 0.5f), "axle diffs keep a symmetric stock bias (no one-sided torque)");
        Check(Near(cDiff.biasAB, 0.4f * 1.5f), "centre diff bias scaled (front/rear split)");
        Check(Near(rDiff.stiffness, 1f) && Near(fDiff.stiffness, 1f), "diff stiffness capped at 1.0 (0.8 x 2 would wind up)");

        // No delegate churn: repeated applies do not re-assign an unchanged type.
        int before = rDiff.TypeAssignments;
        tuner.ApplyLive();
        tuner.ApplyLive();
        Check(rDiff.TypeAssignments == before, "unchanged diff type is not re-assigned (setter allocates a delegate)");

        // Back to Stock while enabled: forced types are undone, not left Locked.
        DrivetrainSettings.SetPresetByName("Stock");
        tuner.ApplyLive();
        Check(rearDiff.DifferentialType == DifferentialComponent.Type.Open && rDiff.DifferentialType == DifferentialComponent.Type.Open,
            "switching to Stock while enabled restores the stock diff types");
        Check(Near(cDiff.biasAB, 0.4f) && Near(rDiff.stiffness, 0.8f), "Stock restores bias/stiffness");

        // Shift points.
        awd.powertrain.engine.revLimiterRPM = 4700f;
        UnityEngine.Object.Registry.Clear();
        VehicleController close = MakeCar(out FakeWheel[] _, 0f);
        close.powertrain.transmission.UpshiftRPM = 4500f;    // 96% of the 4700 limiter: tight but valid
        close.powertrain.transmission.DownshiftRPM = 3800f;
        UnityEngine.Object.Registry.Add(close);
        var t2 = new VehicleTuner();
        t2.ReapplyNow();
        DrivetrainSettings.SetPresetByName("Race");          // rev x1.10, upshift x1.15
        t2.ApplyLive();
        float rev = close.powertrain.engine.revLimiterRPM;
        float up = close.powertrain.transmission.UpshiftRPM;
        float down = close.powertrain.transmission.DownshiftRPM;
        Check(up <= rev * 0.97f + 0.01f, "Race: upshift kept below 97% of the scaled limiter (" + up + " / " + rev + ")");
        Check(down < up, "downshift stays below upshift");
        DrivetrainPreset cu = DrivetrainSettings.BeginEdit();
        cu.UpshiftScale = 0.8f;
        cu.DownshiftScale = 1.2f;
        t2.ApplyLive();
        Check(close.powertrain.transmission.DownshiftRPM <= close.powertrain.transmission.UpshiftRPM * 0.9f + 0.01f,
            "down x1.2 / up x0.8: downshift capped below upshift (no gear hunting)");
        DrivetrainSettings.SetPresetByName("Stock");
        t2.ApplyLive();
        Check(Near(close.powertrain.transmission.UpshiftRPM, 4500f) && Near(close.powertrain.transmission.DownshiftRPM, 3800f),
            "Stock preset reproduces the shipped shift points exactly");
        DrivetrainSettings.Enabled = false;
        t2.ApplyLive();
        tuner.ApplyLive();
        Check(Near(close.powertrain.transmission.UpshiftRPM, 4500f), "disable restores shift points");
        DrivetrainSettings.ResetAll();
    }

    private static void TestAeroFidelity()
    {
        UnityEngine.Object.Registry.Clear();
        AeroSettings.ResetAll();

        // Shipped module with drag AND downforce switched off by its designer.
        var vcOff = MakeCar(out FakeWheel[] _, 0f);
        var mod = new AerodynamicsModule
        {
            simulateDrag = false,
            simulateDownforce = false,
            downforcePoints = { new DownforcePoint { maxForce = 3000f } }
        };
        vcOff.moduleManager.Components.Add(mod);
        UnityEngine.Object.Registry.Add(vcOff);

        // Vehicle without a module, whose state settings load the module as "enabled"
        // but uninitialised (it would never run unless VC_Enable initialises it).
        var vcNone = MakeCar(out FakeWheel[] _, 0f);
        UnityEngine.Object.Registry.Add(vcNone);

        // Vehicle without a module manager at all: must not throw.
        var vcNoMgr = MakeCar(out FakeWheel[] _, 0f);
        vcNoMgr.moduleManager = null;
        UnityEngine.Object.Registry.Add(vcNoMgr);

        var tuner = new VehicleTuner();
        AeroSettings.Enabled = true;
        AeroSettings.SetPresetByName("Stock");
        tuner.ReapplyNow();
        Check(!mod.simulateDrag && !mod.simulateDownforce, "Stock keeps a shipped module's drag/downforce switches OFF");

        AeroSettings.SetPresetByName("Race");
        NWH.VehiclePhysics2.Modules.ManagerVehicleComponent.OnboardEnablesState = true;
        bool threw = false;
        try { tuner.ApplyLive(); } catch (Exception) { threw = true; }
        NWH.VehiclePhysics2.Modules.ManagerVehicleComponent.OnboardEnablesState = false;
        Check(!threw, "vehicle without a module manager does not break the apply pass");
        Check(!mod.simulateDrag && !mod.simulateDownforce, "Race never switches on effects the designer turned off");
        AerodynamicsModule added = FindOf<AerodynamicsModule>(vcNone);
        Check(added.IsActive && added.simulateDrag, "onboarded module is initialised and active (state settings said enabled)");

        // Back to Stock while enabled: the vehicle had no aero, so the module must go inert.
        AeroSettings.SetPresetByName("Stock");
        tuner.ApplyLive();
        Check(!added.state.isEnabled && !added.simulateDrag && !added.simulateDownforce,
            "Stock while enabled parks the onboarded module (no default drag added)");

        AeroSettings.SetPresetByName("Race");
        tuner.ApplyLive();
        Check(added.IsActive && added.simulateDrag, "Race again re-activates the same module");
        AeroSettings.Enabled = false;
        tuner.ApplyLive();
        added.VC_Enable(false);   // e.g. NWH LOD switching the module back on after our restore
        Check(!added.simulateDrag && !added.simulateDownforce, "restored onboarded module is inert even if something re-enables it");
        Check(CountOf<AerodynamicsModule>(vcNone) == 1, "still exactly one onboarded aero module");
        AeroSettings.ResetAll();
    }

    private static void TestLifecycle()
    {
        UnityEngine.Object.Registry.Clear();
        SuspensionSettings.ResetAll();
        DrivetrainSettings.ResetAll();
        VehicleController vc = MakeCar(out FakeWheel[] w, 0f);
        UnityEngine.Object.Registry.Add(vc);

        var tuner = new VehicleTuner();
        SuspensionSettings.Enabled = true;
        SuspensionSettings.SetPresetByName("Race");
        tuner.ReapplyNow();
        float raceSpring = w[0].SpringMaxForce;
        Check(!Near(raceSpring, 30000f), "suspension applied");

        // Something (the game) changes a field of a category we never enabled.
        vc.powertrain.engine.maxPower = 150f;

        // Runner deactivated (not destroyed): the tuner must hand the vehicles back.
        Invoke(tuner, "OnDisable");
        Check(Near(w[0].SpringMaxForce, 30000f), "OnDisable restores applied categories");
        Check(Near(vc.powertrain.engine.maxPower, 150f), "categories that were never applied are not clobbered");

        // A replacement tuner now captures true stock: no compounding.
        var fresh = new VehicleTuner();
        fresh.ReapplyNow();
        Check(Near(w[0].SpringMaxForce, raceSpring), "replacement tuner applies Race on true stock (no compounding)");
        SuspensionSettings.Enabled = false;
        fresh.ApplyLive();
        Check(Near(w[0].SpringMaxForce, 30000f), "replacement tuner restores to the real stock value");

        vc.powertrain.engine.maxPower = 175f;   // the game changes it after this tuner's capture
        Invoke(fresh, "OnDestroy");
        Check(Near(vc.powertrain.engine.maxPower, 175f), "OnDestroy leaves never-applied categories alone");
        SuspensionSettings.ResetAll();
    }

    private static void TestConfigHardening(string dir)
    {
        string path = Path.Combine(dir, "hard.cfg");
        File.WriteAllText(path, "[Suspension]\nPreset = Street\n\n[Drivetrain.Custom]\nDiffFrontMode = 7\nDiffRearMode = lsd\n");
        ModConfig.Load(new ConfigFile(path, true));
        Check(DrivetrainPreset.Custom.DiffFrontMode == DiffMode.Stock, "numeric DiffFrontMode '7' -> Stock (was an undefined enum value)");
        Check(DrivetrainPreset.Custom.DiffRearMode == DiffMode.LimitedSlip, "'lsd' (the panel label) -> LimitedSlip");
        string txt = File.ReadAllText(path);
        Check(txt.Contains("Preset = Stock") && !txt.Contains("Preset = Street"), "legacy 'Street' rewritten to Stock on load");

        File.WriteAllText(path, "[Drivetrain.Custom]\nDiffFrontMode =  Locked \nDiffRearMode = Open\n");
        ModConfig.Load(new ConfigFile(path, true));
        Check(DrivetrainPreset.Custom.DiffFrontMode == DiffMode.Locked && DrivetrainPreset.Custom.DiffRearMode == DiffMode.Open,
            "named modes still parse (case/space tolerant)");

        // UI helper: linking front/rear only forks when the axles differ.
        SuspensionSettings.SetPresetByName("Comfort");
        Check(!SuspensionSettings.AxlesDiffer(), "Comfort has equal axles (split OFF does not fork it into Custom)");
        SuspensionSettings.SetPresetByName("Race");
        Check(SuspensionSettings.AxlesDiffer(), "Race has different axles");
        SuspensionSettings.ResetAll();
        DrivetrainSettings.ResetAll();
    }

    private static void TestEuroTruck(string dir)
    {
        SteeringPreset euro = SteeringPreset.FindBuiltIn("Euro Truck");
        Check(euro != null, "Euro Truck preset exists");
        Check(SteeringPreset.FindBuiltIn("Truck-sim") == null, "Truck-sim preset removed");
        Check(SteeringPreset.Presets.Length == 6, "still 6 steering presets");
        Check(SteeringPreset.Presets[0] == SteeringPreset.Vanilla
              && SteeringPreset.Presets[1].Name == "GTA-style Keyboard"
              && SteeringPreset.Presets[2].Name == "Euro Truck"
              && SteeringPreset.Presets[3].Name == "Sim/Race"
              && SteeringPreset.Presets[4].Name == "Drift"
              && SteeringPreset.Presets[5] == SteeringPreset.Custom,
            "preset order: Vanilla, GTA-style Keyboard, Euro Truck, Sim/Race, Drift, Custom");
        Check(Near(euro.RateMultiplier, 0.5f) && Near(euro.SmoothingScale, 1.7f)
              && Near(euro.SlipAngleDeg, 6.5f) && Near(euro.OppositeLockBoost, 1f)
              && Near(euro.LinearityExponent, 1.35f),
            "Euro Truck values: rate 0.5, smoothing 1.7, slip 6.5, opp-lock 1.0, linearity 1.35");
        Check(euro.TractionClampEnabled && !euro.UseVehicleCurve && euro.LinearityOverride,
            "Euro Truck enables the traction clamp, uses its own lock curve, and the linearity override");

        // Exact preset curves (0.4.0 conversions).
        Check(SteeringPreset.FindBuiltIn("GTA-style Keyboard").LockCurve.Serialize() == "0:1;0.35:0.45;1:0.15", "GTA lock curve exact");
        Check(euro.LockCurve.Serialize() == "0:1;0.2:0.6;0.5:0.3;1:0.12", "Euro Truck lock curve exact");
        Check(SteeringPreset.FindBuiltIn("Sim/Race").LockCurve.Serialize() == "0:1;0.5:0.35;1:0.22", "Sim/Race lock curve exact");
        Check(SteeringPreset.FindBuiltIn("Drift").LockCurve.Serialize() == "0:1;0.4:0.55;1:0.3", "Drift lock curve exact");
        Check(Near(euro.LockCurve.Evaluate(0f), 1f) && Near(euro.LockCurve.Evaluate(0.5f), 0.3f) && Near(euro.LockCurve.Evaluate(1f), 0.12f),
            "Euro Truck lock curve: 1 @ 0, 0.3 @ 0.5, 0.12 @ 1");

        // Return curves: flat 1 everywhere except Euro Truck's hold-then-straighten ramp.
        Check(euro.ReturnCurve.Serialize() == "0:0;0.3:0.4;1:0.7", "Euro Truck return curve exact (hold at rest, straighten with speed)");
        Check(Near(euro.ReturnCurve.Evaluate(0f), 0f) && Near(euro.ReturnCurve.Evaluate(0.3f), 0.4f) && Near(euro.ReturnCurve.Evaluate(1f), 0.7f),
            "Euro Truck return curve: 0 @ rest, 0.4 @ mid, 0.7 @ top");
        Check(SteeringPreset.FindBuiltIn("GTA-style Keyboard").ReturnCurve.Serialize() == "0:1;1:1"
              && SteeringPreset.FindBuiltIn("Sim/Race").ReturnCurve.Serialize() == "0:1;1:1"
              && SteeringPreset.FindBuiltIn("Drift").ReturnCurve.Serialize() == "0:1;1:1"
              && SteeringPreset.Custom.ReturnCurve.Serialize() == "0:1;1:1",
            "other presets keep symmetric return (flat 1)");

        // Config defaults parse to the template curves.
        EditableCurve parsedLock;
        Check(EditableCurve.TryParse(EditableCurve.DefaultLockCurveText, out parsedLock)
              && parsedLock.Serialize() == EditableCurve.DefaultLockCurveText, "DefaultLockCurveText round-trips");
        EditableCurve parsedReturn;
        Check(EditableCurve.TryParse(EditableCurve.DefaultReturnCurveText, out parsedReturn)
              && parsedReturn.Serialize() == EditableCurve.DefaultReturnCurveText, "DefaultReturnCurveText round-trips");

        SteeringSettings.SetPresetByName("Truck-sim");
        Check(SteeringSettings.ActivePreset == euro, "runtime SetByName('Truck-sim') resolves to Euro Truck");
        SteeringSettings.SetPresetByName("Custom");
        SteeringSettings.ResetAll();

        // Forking a built-in preset clones its curves: Custom edits never touch the preset.
        SteeringSettings.SetPresetByName("Drift");
        SteeringPreset driftCopy = SteeringSettings.BeginEdit();
        driftCopy.LockCurve.TryMovePoint(0, 0.5f, 0.5f);
        Check(SteeringPreset.FindBuiltIn("Drift").LockCurve.Serialize() == "0:1;0.4:0.55;1:0.3",
            "editing Custom's clone leaves the Drift preset curve pristine");
        SteeringSettings.ResetAll();

        // Custom curves persist through the config file (own file: _config points
        // at whatever the previous test loaded).
        string rt = Path.Combine(dir, "return.cfg");
        File.WriteAllText(rt, "");
        ModConfig.Load(new ConfigFile(rt, true));
        SteeringPreset.Custom.UseVehicleCurve = false;
        SteeringPreset.Custom.LockCurve = EditableCurve.FromPoints(0f, 1f, 0.5f, 0.4f, 1f, 0.1f);
        SteeringPreset.Custom.ReturnCurve = EditableCurve.FromPoints(0f, 0f, 1f, 0.6f);
        ModConfig.Save();
        string txt2 = File.ReadAllText(rt);
        Check(txt2.Contains("LockCurve = ") && txt2.Contains("ReturnCurve = ") && txt2.Contains("UseVehicleCurve = false"),
            "curve keys written to the cfg");
        SteeringSettings.ResetAll();
        ModConfig.Load(new ConfigFile(rt, true));
        Check(!SteeringPreset.Custom.UseVehicleCurve
              && SteeringPreset.Custom.LockCurve.Serialize() == "0:1;0.5:0.4;1:0.1"
              && SteeringPreset.Custom.ReturnCurve.Serialize() == "0:0;1:0.6",
            "curve config round-trip");
        SteeringSettings.ResetAll();
    }

    private static void TestLegacySteeringMigration(string dir)
    {
        // A real 0.3.0 file: legacy preset name + the two legacy curve keys.
        string path = Path.Combine(dir, "steer-mig.cfg");
        File.WriteAllText(path,
            "[Steering]\nEnabled = true\nPreset = Truck-sim\n\n[Steering.Custom]\nBasedOn = Truck-sim\nRateMultiplier = 1.2\nSpeedCurveScale = 0.5\nCenterReturnScale = 0.4\n");
        ModConfig.Load(new ConfigFile(path, true));
        Check(SteeringSettings.ActivePreset == SteeringPreset.FindBuiltIn("Euro Truck"),
            "saved 'Truck-sim' preset loads as Euro Truck");
        Check(SteeringPreset.Custom.BasedOn == "Euro Truck",
            "saved Custom.BasedOn 'Truck-sim' rewritten to Euro Truck");
        Check(!SteeringPreset.Custom.UseVehicleCurve
              && Near(SteeringPreset.Custom.LockCurve.Evaluate(0.5f), 0.15f),
            "legacy SpeedCurveScale x0.5 folded into the Euro Truck lock curve");
        Check(Near(SteeringPreset.Custom.ReturnCurve.Evaluate(0f), 0.4f)
              && SteeringPreset.Custom.ReturnCurve.Serialize() == "0:0.4;1:0.4",
            "legacy CenterReturnScale 0.4 became a flat return curve");
        Check(Near(SteeringPreset.Custom.RateMultiplier, 1.2f), "Custom values survive the migration");
        string txt = File.ReadAllText(path);
        Check(txt.Contains("Preset = Euro Truck") && !txt.Contains("Truck-sim"),
            "cfg file rewritten to Euro Truck, no Truck-sim left");
        Check(!txt.Contains("SpeedCurveScale") && !txt.Contains("CenterReturnScale"),
            "legacy curve keys removed from the cfg");
        // Reload: the fold must not run twice (values stay as-is).
        ModConfig.Load(new ConfigFile(path, true));
        Check(Near(SteeringPreset.Custom.LockCurve.Evaluate(0.5f), 0.15f)
              && Near(SteeringPreset.Custom.ReturnCurve.Evaluate(0f), 0.4f),
            "reload does not double-apply the fold");
        SteeringSettings.ResetAll();
    }

    private static void TestSteeringCurveMigration(string dir)
    {
        // (b) vehicle-curve user (BasedOn "") with legacy keys: scale is dropped (documented).
        string path = Path.Combine(dir, "curve-mig-b.cfg");
        File.WriteAllText(path, "[Steering.Custom]\nSpeedCurveScale = 2\nCenterReturnScale = 1\n");
        ModConfig.Load(new ConfigFile(path, true));
        Check(SteeringPreset.Custom.UseVehicleCurve, "BasedOn '' keeps the vehicle curve");
        Check(SteeringPreset.Custom.LockCurve.Serialize() == EditableCurve.DefaultLockCurveText,
            "template lock curve when no preset to fold into");
        Check(SteeringPreset.Custom.ReturnCurve.Serialize() == "0:1;1:1", "legacy return 1 -> flat 1");
        Check(!File.ReadAllText(path).Contains("SpeedCurveScale"), "legacy keys removed (b)");
        SteeringSettings.ResetAll();

        // (c) clobber regression: a 0.4.0-style file (no legacy keys) with edited curves
        // must not be touched by the fold.
        string pathC = Path.Combine(dir, "curve-mig-c.cfg");
        File.WriteAllText(pathC,
            "[Steering.Custom]\nBasedOn = Euro Truck\nUseVehicleCurve = false\n"
            + "LockCurve = 0:1;0.5:0.2;1:0.05\nReturnCurve = 0:0;1:0.9\n");
        ModConfig.Load(new ConfigFile(pathC, true));
        Check(SteeringPreset.Custom.LockCurve.Serialize() == "0:1;0.5:0.2;1:0.05"
              && SteeringPreset.Custom.ReturnCurve.Serialize() == "0:0;1:0.9",
            "0.4.0-style curves are not clobbered by the legacy fold");
        SteeringSettings.ResetAll();

        // (d) garbage LockCurve falls back to the BasedOn preset's curve.
        string pathD = Path.Combine(dir, "curve-mig-d.cfg");
        File.WriteAllText(pathD, "[Steering.Custom]\nBasedOn = Euro Truck\nLockCurve = garbage\n");
        ModConfig.Load(new ConfigFile(pathD, true));
        Check(SteeringPreset.Custom.LockCurve.Serialize() == "0:1;0.2:0.6;0.5:0.3;1:0.12",
            "garbage LockCurve falls back to the BasedOn preset's curve");
        SteeringSettings.ResetAll();
    }

    private static void TestEditableCurve()
    {
        EditableCurve c = EditableCurve.FromPoints(0f, 1f, 0.5f, 0.35f, 1f, 0.22f);
        Check(Near(c.Evaluate(0f), 1f) && Near(c.Evaluate(0.5f), 0.35f) && Near(c.Evaluate(1f), 0.22f),
            "Evaluate at keyframes");
        Check(Near(c.Evaluate(0.25f), 0.675f), "Evaluate lerps between points");
        Check(Near(c.Evaluate(-1f), 1f) && Near(c.Evaluate(2f), 0.22f), "Evaluate clamps t to [0,1]");
        Check(Near(EditableCurve.Flat(0.4f).Evaluate(0.7f), 0.4f), "Flat curve");

        Check(c.TryAddPoint(0.25f, 0.9f) && c.Count == 4 && Near(c.X(1), 0.25f) && Near(c.Y(1), 0.9f),
            "add inserts sorted");
        Check(!c.TryAddPoint(0.25f, 0.5f), "add rejects duplicate x");
        for (int i = 0; i < 6; i++)
        {
            c.TryAddPoint(0.02f + i * 0.02f, 0.5f);
        }
        Check(c.Count == EditableCurve.MaxPoints && !c.TryAddPoint(0.99f, 0.5f), "add rejects beyond MaxPoints");
        Check(!c.TryRemovePoint(99), "remove rejects bad index");
        c.TryRemovePoint(1);
        Check(c.Count == EditableCurve.MaxPoints - 1, "remove works");

        EditableCurve mv = EditableCurve.FromPoints(0f, 0f, 0.5f, 0.5f, 1f, 1f);
        Check(mv.TryMovePoint(1, 2f, 5f) && Near(mv.X(1), 1f) && Near(mv.Y(1), 1f),
            "move clamps x between neighbours and y to [0,1]");

        string before = c.Serialize();
        EditableCurve clone = c.Clone();
        clone.TryMovePoint(0, 0f, 0f);
        Check(clone.Serialize() != before, "clone is editable");
        Check(c.Serialize() == before, "clone edit does not touch the original");

        EditableCurve copy = EditableCurve.Flat(1f);
        copy.CopyFrom(c);
        copy.TryMovePoint(0, 0f, 0f);
        Check(c.Serialize() == before, "CopyFrom copies arrays (no aliasing)");

        EditableCurve scaled = EditableCurve.FromPoints(0f, 1f, 1f, 0.5f);
        scaled.ScaleY(2f);
        Check(Near(scaled.Y(0), 1f) && Near(scaled.Y(1), 1f), "ScaleY clamps to [0,1]");

        // Serialize/parse round-trip with float precision.
        EditableCurve precise = EditableCurve.FromPoints(0f, 0f, 1f / 3f, 0.33333334f, 1f, 1f);
        EditableCurve back;
        Check(EditableCurve.TryParse(precise.Serialize(), out back) && back.Serialize() == precise.Serialize(),
            "serialize/parse round-trip preserves values");

        EditableCurve bad;
        Check(!EditableCurve.TryParse("", out bad) && bad == null, "parse rejects empty");
        Check(!EditableCurve.TryParse("abc", out bad), "parse rejects garbage");
        Check(!EditableCurve.TryParse("0:1", out bad), "parse rejects 1 point");
        Check(!EditableCurve.TryParse("0:1;0.1:1;0.2:1;0.3:1;0.4:1;0.5:1;0.6:1;0.7:1;0.8:1", out bad), "parse rejects 9 points");
        EditableCurve dup;
        Check(EditableCurve.TryParse("0:1;0:1;1:0.5", out dup) && dup.Count == 2 && Near(dup.Y(0), 1f),
            "parse dedupes near-equal x");
        EditableCurve clamped;
        Check(EditableCurve.TryParse("2:3;4:5;0.5:0.5", out clamped)
              && Near(clamped.X(0), 0.5f) && Near(clamped.Y(0), 0.5f)
              && Near(clamped.X(1), 1f) && Near(clamped.Y(1), 1f),
            "parse clamps x/y to [0,1] and dedupes clamped-equal x");
        EditableCurve commas;
        Check(EditableCurve.TryParse("0,5:0,25;1:1", out commas) && Near(commas.X(0), 0.5f) && Near(commas.Y(0), 0.25f),
            "parse accepts comma decimals");
    }

    // ---------------------------------------------------------------- 0.5.0

    private static void TestCurveRoundTrip()
    {
        // Dragging a point onto its neighbour's x used to create a vertical step that
        // TryParse deduplicates: the reloaded curve had one point less than the saved one.
        EditableCurve c = EditableCurve.FromPoints(0f, 0f, 0.5f, 0.5f, 1f, 1f);
        c.TryMovePoint(1, 2f, 0.8f);                    // drag far right, onto x = 1
        EditableCurve back;
        Check(EditableCurve.TryParse(c.Serialize(), out back) && back.Count == c.Count && back.SameAs(c, 1e-6f),
            "point dragged onto its neighbour survives save + reload (count " + (back != null ? back.Count : 0) + ")");
        Check(c.X(1) < c.X(2), "moved point keeps a gap to its neighbour (x " + c.X(1).ToString("R") + ")");
        c.TryMovePoint(1, -5f, 0.2f);                   // and onto the left neighbour
        Check(EditableCurve.TryParse(c.Serialize(), out back) && back.Count == 3 && c.X(1) > c.X(0),
            "left neighbour likewise");

        // Neighbours already closer than the gap (a parsed file): y moves, x stays put.
        EditableCurve tight;
        EditableCurve.TryParse("0:0;0.00015:0.5;0.0003:1;1:1", out tight);
        float x1 = tight.X(1);
        Check(tight.TryMovePoint(1, 0.9f, 0.7f) && Near(tight.X(1), x1, 1e-7f) && Near(tight.Y(1), 0.7f),
            "tight neighbours: y moves, x stays (order can never break)");

        EditableCurve a = EditableCurve.FromPoints(0f, 1f, 0.5f, 0.35f, 1f, 0.22f);
        Check(a.SameAs(a.Clone()) && !a.SameAs(EditableCurve.Flat(1f)) && !a.SameAs(null), "SameAs: equal / different count / null");
        EditableCurve moved = a.Clone();
        moved.TryMovePoint(1, 0.5f, 0.36f);
        Check(!a.SameAs(moved), "SameAs: a moved point differs");
    }

    private static void TestAeroOnboardingDrag()
    {
        UnityEngine.Object.Registry.Clear();
        AeroSettings.ResetAll();
        var vc = MakeCar(out FakeWheel[] _, 0f);   // ships WITHOUT an aero module
        UnityEngine.Object.Registry.Add(vc);
        var tuner = new VehicleTuner();
        AeroSettings.Enabled = true;

        // Street = x0.8 downforce, x1.0 ("stock") drag: nothing to add to a vehicle without aero.
        AeroSettings.SetPresetByName("Street");
        tuner.ReapplyNow();
        Check(CountOf<AerodynamicsModule>(vc) == 0, "Street (drag x1.0) onboards nothing on an aero-less vehicle (0.4.0: full 0.35 Cd)");

        // Less drag than stock cannot ADD drag.
        AeroPreset custom = AeroSettings.BeginEdit();
        custom.DragScale = 0.8f;
        custom.DownforceScale = 1.5f;
        tuner.ApplyLive();
        Check(CountOf<AerodynamicsModule>(vc) == 0, "drag x0.8 (+ downforce x1.5) adds no module/drag (0.4.0 added 0.28 Cd)");

        custom.DragScale = 1.2f;
        tuner.ApplyLive();
        AerodynamicsModule m = CountOf<AerodynamicsModule>(vc) == 1 ? FindOf<AerodynamicsModule>(vc) : null;
        Check(m != null && m.simulateDrag && !m.simulateDownforce && Near(m.frontalCd, 0.35f * 0.2f) && Near(m.sideCd, 1.05f * 0.2f),
            "drag x1.2 onboards drag = default Cd x 0.2 (continuous at x1.0), never downforce");

        custom.DragScale = 0.9f;
        tuner.ApplyLive();
        Check(m != null && !m.state.isEnabled && !m.simulateDrag, "dropping back below x1.0 parks the onboarded module");
        AeroSettings.ResetAll();
        tuner.ApplyLive();
    }

    private static void TestBaselineRefresh()
    {
        UnityEngine.Object.Registry.Clear();
        SuspensionSettings.ResetAll();
        DrivetrainSettings.ResetAll();
        BrakesSettings.ResetAll();
        VehicleController vc = MakeCar(out FakeWheel[] w, 0f);
        UnityEngine.Object.Registry.Add(vc);
        var tuner = new VehicleTuner();
        tuner.ReapplyNow();                          // first sight: everything captured, nothing applied

        // The game changes fields of categories that are OFF.
        vc.powertrain.engine.maxPower = 175f;
        w[0].SpringMaxForce = 40000f;
        vc.brakes.maxTorque = 9000f;

        DrivetrainSettings.Enabled = true;
        DrivetrainSettings.SetPresetByName("Race");
        DrivetrainPreset race = DrivetrainSettings.ActivePreset;
        SuspensionSettings.Enabled = true;
        SuspensionSettings.SetPresetByName("Race");
        float springF = SuspensionSettings.Spring(true);
        BrakesSettings.Enabled = true;
        BrakesSettings.SetPresetByName("Stock");
        tuner.ApplyLive();
        Check(Near(vc.powertrain.engine.maxPower, 175f * race.PowerScale), "enable scales the game's CURRENT power (0.4.0: first-sight value)");
        Check(Near(w[0].SpringMaxForce, 40000f * springF), "enable scales the game's current spring");
        Check(Near(vc.brakes.maxTorque, 9000f), "Stock brakes keep the game's current torque");

        // While applied, a later game change is NOT re-read (it would read our own values).
        tuner.ApplyLive();
        Check(Near(vc.powertrain.engine.maxPower, 175f * race.PowerScale), "re-apply while on does not compound");

        DrivetrainSettings.Enabled = false;
        SuspensionSettings.Enabled = false;
        BrakesSettings.Enabled = false;
        tuner.ApplyLive();
        Check(Near(vc.powertrain.engine.maxPower, 175f) && Near(w[0].SpringMaxForce, 40000f) && Near(vc.brakes.maxTorque, 9000f),
            "disable restores the game's value, not the first-sight one (0.4.0: 150 / 30000 / 7000)");
        SuspensionSettings.ResetAll();
        DrivetrainSettings.ResetAll();
        BrakesSettings.ResetAll();
    }

    private static void TestCurveEditorLayout()
    {
        // The stubs have no layout engine, so check the bands the row is built from.
        float titleBottom = CurveEditor.TitleTop + CurveEditor.TitleHeight;
        float resetBottom = CurveEditor.TitleTop + CurveEditor.ResetHeight;
        float hintBottom = CurveEditor.HintTop + CurveEditor.HintHeight;
        Check(resetBottom <= CurveEditor.GraphTop && titleBottom <= CurveEditor.GraphTop && hintBottom <= CurveEditor.GraphTop,
            "header (title, readout, Reset, hint) ends above the graph (0.4.0: Reset sat under the graph, unclickable)");
        Check(resetBottom <= CurveEditor.HintTop && titleBottom <= CurveEditor.HintTop, "hint starts below the title/Reset line");
        Check(CurveEditor.ReadoutRight >= CurveEditor.ResetRight + CurveEditor.ResetWidth, "readout sits left of Reset");
        float graphHeight = CurveEditor.RowHeight - CurveEditor.GraphTop - CurveEditor.GraphBottom;
        Check(graphHeight >= 150f, "graph keeps a usable height (" + graphHeight + " px)");
    }

    private static void TestCurveEditorRouting()
    {
        Check(CurveEditor.RouteDrag(true, 2, true) == CurveEditor.DragRoute.MovePoint, "drag on a handle moves the point");
        Check(CurveEditor.RouteDrag(true, -1, true) == CurveEditor.DragRoute.ScrollList, "drag on empty graph scrolls the list (0.4.0: swallowed)");
        Check(CurveEditor.RouteDrag(false, 2, true) == CurveEditor.DragRoute.ScrollList, "disabled curve: even a handle drag scrolls");
        Check(CurveEditor.RouteDrag(true, -1, false) == CurveEditor.DragRoute.None, "no scroll view: nothing");

        Check(CurveEditor.RouteClick(true, false, true, 1, -1) == CurveEditor.ClickAction.AddPoint, "click on empty space adds");
        Check(CurveEditor.RouteClick(true, false, true, 2, 1) == CurveEditor.ClickAction.RemovePoint, "double-click on a handle removes");
        Check(CurveEditor.RouteClick(true, true, true, 1, -1) == CurveEditor.ClickAction.None, "release after a drag never adds a point (0.4.0 did)");
        Check(CurveEditor.RouteClick(false, false, true, 1, -1) == CurveEditor.ClickAction.None, "disabled (OFF/Vanilla) graph ignores clicks (0.4.0 forked to Custom)");
        Check(CurveEditor.RouteClick(true, false, false, 1, -1) == CurveEditor.ClickAction.None, "right click does nothing");
        Check(CurveEditor.RouteClick(true, false, true, 1, 0) == CurveEditor.ClickAction.None, "single click on a handle does nothing");
    }

    private static void TestApocasetterKey(string dir)
    {
        string path = Path.Combine(dir, "apocasetter.cfg");
        File.WriteAllText(path, "");
        ModConfig.Load(new ConfigFile(path, true));
        string txt = File.ReadAllText(path);
        Check(txt.Contains("[General]") && txt.Contains("Apocasetter = true"),
            "Apocasetter opt-in key written to the cfg ([General] Apocasetter = true)");
    }

    private static void TestUiStrings()
    {
        bool templatesOk = true;
        foreach (FieldInfo f in typeof(UiStrings).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (f.FieldType != typeof(string) || !f.IsInitOnly)
            {
                continue;
            }
            string v = (string)f.GetValue(null);
            if (v == null || !v.Contains("{"))
            {
                continue;
            }
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] == '{')
                {
                    int close = v.IndexOf('}', i);
                    if (close < 0 || v.Substring(i, close - i + 1) != "{0}")
                    {
                        templatesOk = false;
                        Console.WriteLine("  bad template " + f.Name);
                    }
                    i = close;
                }
                else if (v[i] == '}')
                {
                    templatesOk = false;
                    Console.WriteLine("  stray } in " + f.Name);
                }
            }
        }
        Check(templatesOk, "every UiStrings template uses only {0} placeholders");
        Check(UiStrings.Times(1.4f) == "×1.40", "Times(1.4) = ×1.40");
        Check(UiStrings.Force(42000f) == 42000f.ToString("N0") + " N", "Force(42000) = N0-grouped number + ' N'");
        Check(UiStrings.Rate(3800f) == 3800f.ToString("N0") + " N·s/m", "Rate(3800) = N0-grouped number + ' N·s/m'");
        Check(UiStrings.Length(0.3f) == "30 cm", "Length(0.3) = 30 cm");
        Check(UiStrings.Percent(0.25f) == "25%", "Percent(0.25) = 25%");
        Check(UiStrings.Deg(6.5f) == "6.5 deg", "Deg(6.5) = 6.5 deg");
        Check(UiStrings.SpeedMps(2f) == "2.0 m/s", "SpeedMps(2) = 2.0 m/s");
        Check(UiStrings.CustomPresetFmt.IndexOf("{0}") >= 0 && UiStrings.CustomPresetFmt.IndexOf("{0}", UiStrings.CustomPresetFmt.IndexOf("{0}") + 1) < 0,
            "CustomPresetFmt has exactly one {0}");
    }

    // ================================================================ 0.6.0

    private static void ResetAllCategories()
    {
        SteeringSettings.ResetAll();
        SuspensionSettings.ResetAll();
        AeroSettings.ResetAll();
        BrakesSettings.ResetAll();
        GripSettings.ResetAll();
        DrivetrainSettings.ResetAll();
        AssistsSettings.ResetAll();
        AlignmentSettings.ResetAll();
        GearboxSettings.ResetAll();
    }

    private static void TestAudit060(string dir)
    {
        // (a) A vehicle destroyed between scans: ApplyLive (every slider tick) must not write
        //     into it — 0.5.0 onboarded an aero module into the destroyed vehicle.
        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        var tuner = new VehicleTuner();
        VehicleController dead = MakeCar(out FakeWheel[] _, 0f);
        UnityEngine.Object.Registry.Add(dead);
        tuner.ReapplyNow();
        Check(tuner.TrackedVehicles == 1, "vehicle tracked before it is destroyed");
        dead.TestDestroyed = true;   // Unity fake-null: the object compares == null from now on
        AeroSettings.Enabled = true;
        AeroSettings.SetPresetByName("Race");
        bool threw = false;
        try { tuner.ApplyLive(); } catch (Exception) { threw = true; }
        Check(!threw && CountOf<AerodynamicsModule>(dead) == 0,
            "ApplyLive skips a vehicle destroyed since the last scan (0.5.0 onboarded aero into it)");
        Check(tuner.TrackedVehicles == 0, "destroyed vehicle forgotten by ApplyLive, not only by the 2 s scan");
        AeroSettings.ResetAll();
        tuner.ApplyLive();

        // (b) ABS vs a LATCHED handbrake (HandbrakeType.Latching keeps handbrakeValue after release).
        UnityEngine.Object.Registry.Clear();
        VehicleController vc = MakeCar(out FakeWheel[] w, 0f);
        UnityEngine.Object.Registry.Add(vc);
        AssistsSettings.Enabled = true;
        AssistsSettings.SetPresetByName("Standard");
        tuner = new VehicleTuner();
        tuner.ReapplyNow();
        Brakes.BrakeTorqueModifier abs = vc.brakes.brakeTorqueModifiers[vc.brakes.brakeTorqueModifiers.Count - 1];
        w[2].SetLongitudinalSlip(0.5f);   // rear wheel locked by the handbrake
        vc.input.Handbrake = 0f;          // input released ...
        vc.brakes.handbrakeValue = 1f;    // ... but the latching handbrake is still on
        Check(Near(abs(), 1f), "ABS stands down while a latched handbrake is applied (0.5.0 cut it to 1%)");
        vc.brakes.handbrakeValue = 0f;
        Check(abs() < 0.5f, "ABS still releases a locked wheel when no handbrake is applied");
        w[2].SetLongitudinalSlip(0f);

        // (c) TCS does not cut power during a gear shift (NWH's TCSModule guard).
        EngineComponent.PowerModifier tcs = vc.powertrain.engine.powerModifiers[vc.powertrain.engine.powerModifiers.Count - 1];
        w[2].SetLongitudinalSlip(-0.5f);  // spinning
        vc.powertrain.transmission.isShifting = true;
        Check(Near(tcs(), 1f), "TCS does not cut while the gearbox is shifting (0.5.0 did: stumble on upshift)");
        vc.powertrain.transmission.isShifting = false;
        Check(tcs() < 0.5f, "TCS still cuts wheelspin outside a shift");
        w[2].SetLongitudinalSlip(0f);
        AssistsSettings.ResetAll();
        tuner.ApplyLive();

        // (d) An external config edit (Apocasetter) must not revert unsaved panel edits.
        string path = Path.Combine(dir, "merge.cfg");
        File.WriteAllText(path, "");
        var cfg = new ConfigFile(path, true);
        ModConfig.Load(cfg);
        SuspensionSettings.Enabled = true;
        SuspensionSettings.SetPresetByName("Sport");
        SuspensionSettings.BeginEdit().SpringFront = 1.7f;        // panel edit, not saved yet
        cfg[new ConfigDefinition("Aero", "Enabled")].BoxedValue = true;   // what a config manager does
        Check(AeroSettings.Enabled, "the external edit itself applies");
        Check(SuspensionSettings.Enabled && SuspensionSettings.ActivePreset == SuspensionPreset.Custom
              && Near(SuspensionPreset.Custom.SpringFront, 1.7f),
            "unsaved panel edits survive an external config edit (0.5.0 reverted them to the saved file)");
        ResetAllCategories();
    }

    /// <summary>MakeCar with real local positions (left x &lt; 0), stock camber and a preserved Z euler.</summary>
    private static VehicleController MakeAlignedCar(out FakeWheel[] w)
    {
        VehicleController vc = MakeCar(out w, 0f);
        float[] x = { -0.8f, 0.8f, -0.8f, 0.8f };
        float[] z = { 1.3f, 1.3f, -1.3f, -1.3f };
        for (int i = 0; i < 4; i++)
        {
            w[i].transform.localPosition = new Vector3(x[i], 0.1f, z[i]);
            w[i].transform.localEulerAngles = new Vector3(0f, 0f, 5f);   // a prefab Z rotation that must survive
            w[i].Camber = i < 2 ? -1.2f : -0.6f;
        }
        return vc;
    }

    private static void TestAlignment()
    {
        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        VehicleController vc = MakeAlignedCar(out FakeWheel[] w);
        UnityEngine.Object.Registry.Add(vc);
        WheelGroup front = vc.powertrain.wheelGroups[0], rear = vc.powertrain.wheelGroups[1];
        var tuner = new VehicleTuner();
        tuner.ReapplyNow();

        VehicleTuner.WheelStock fl = tuner.ReferenceWheelStock(WheelRole.FL);
        VehicleTuner.WheelStock rr = tuner.ReferenceWheelStock(WheelRole.RR);
        Check(fl.Valid && fl.LocalPos.x < 0f && Near(fl.Camber, -1.2f), "reference FL = front, x < 0, stock camber captured");
        Check(rr.Valid && rr.LocalPos.x > 0f && Near(rr.Camber, -0.6f), "reference RR = rear, x > 0");

        // Race: camber + caster + toe + lowered.
        AlignmentSettings.Enabled = true;
        AlignmentSettings.SetPresetByName("Race");
        AlignmentPreset race = AlignmentSettings.ActivePreset;
        tuner.ApplyLive();
        Check(Near(w[0].Camber, -1.2f + race.CamberFL) && Near(w[3].Camber, -0.6f + race.CamberRR), "camber = stock + offset per wheel");
        Check(Near(front.CasterAngle, race.CasterF) && Near(rear.CasterAngle, 0f), "caster per axle through WheelGroup.CasterAngle");
        Check(Near(w[0].transform.localEulerAngles.x, -race.CasterF) && Near(w[1].transform.localEulerAngles.x, -race.CasterF),
            "caster: euler X = -caster on both sides (WheelGroup.ApplyGeometryValues)");
        Check(Near(w[0].transform.localEulerAngles.y, race.ToeF) && Near(w[1].transform.localEulerAngles.y, -race.ToeF),
            "toe mirrored L/R: left +toe, right -toe");
        Check(Near(w[0].transform.localEulerAngles.z, 5f) && Near(w[1].transform.localEulerAngles.z, 5f), "euler Z preserved");
        Check(Near(w[0].transform.localPosition.y, 0.1f - race.PosYFL * 0.01f), "ride lowered through the wheel transform (cm -> m; + PosY = taller)");

        // Off-road: track widened OUTWARD on both sides.
        AlignmentSettings.SetPresetByName("Off-road");
        tuner.ApplyLive();
        Check(Near(w[0].transform.localPosition.x, -0.86f) && Near(w[1].transform.localPosition.x, 0.86f),
            "PosX is outward: left -6 cm, right +6 cm (symmetric preset)");
        Check(Near(w[0].transform.localPosition.y, 0.1f - AlignmentSettings.ActivePreset.PosYFL * 0.01f),
            "Off-road + PosY RAISES the car (mount moves down at fixed spring length)");

        // x = 0 crossing clamp.
        Check(Near(VehicleTuner.WheelX(0.1f, -0.3f), VehicleTuner.MinAbsWheelX) && Near(VehicleTuner.WheelX(-0.1f, -0.3f), -VehicleTuner.MinAbsWheelX),
            "a moved wheel never crosses x = 0 (clamped to 1 cm on its own side)");
        Check(Near(VehicleTuner.WheelX(0f, 0.2f), 0f), "a centre wheel (x = 0) has no side and keeps x");

        // Restore exact.
        AlignmentSettings.Enabled = false;
        tuner.ApplyLive();
        bool exact = true;
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = w[i].transform.localPosition, e = w[i].transform.localEulerAngles;
            float[] x = { -0.8f, 0.8f, -0.8f, 0.8f };
            exact &= Near(p.x, x[i]) && Near(p.y, 0.1f) && Near(e.x, 0f) && Near(e.y, 0f) && Near(e.z, 5f) && Near(w[i].Camber, i < 2 ? -1.2f : -0.6f);
        }
        Check(exact && Near(front.CasterAngle, 0f) && Near(front.ToeAngle, 0f), "OFF restores position, angles, camber, caster and toe exactly");

        // Gates: a closed applyCasterAngle is opened while needed and restored on OFF.
        rear.applyCasterAngle = false;
        AlignmentSettings.Enabled = true;
        AlignmentSettings.BeginEdit().CasterR = 2f;
        tuner.ApplyLive();
        Check(rear.applyCasterAngle && Near(w[2].transform.localEulerAngles.x, -2f), "closed caster gate opened while a rear caster offset is applied");
        AlignmentSettings.Enabled = false;
        tuner.ApplyLive();
        Check(!rear.applyCasterAngle && rear.applyToeAngle && Near(w[2].transform.localEulerAngles.x, 0f), "both gates and the wheel angle restored on OFF");
        rear.applyCasterAngle = true;

        // Rescan re-applies after the game clobbers geometry (e.g. a group re-init).
        AlignmentSettings.SetPresetByName("Race");
        AlignmentSettings.Enabled = true;
        tuner.ApplyLive();
        front.CasterAngle = 0f;                                   // something resets the axle
        w[0].transform.localPosition = new Vector3(-0.8f, 0.1f, 1.3f);
        tuner.ReapplyNow();                                       // the 2 s scan path
        Check(Near(front.CasterAngle, race.CasterF) && Near(w[0].transform.localPosition.y, 0.1f - race.PosYFL * 0.01f),
            "the rescan re-applies clobbered caster and position");
        AlignmentSettings.Enabled = false;
        tuner.ApplyLive();

        // Solid axle + CamberController: camber never written there, flagged for the panel.
        UnityEngine.Object.Registry.Clear();
        VehicleController vc2 = MakeAlignedCar(out FakeWheel[] w2);
        vc2.powertrain.wheelGroups[1].isSolid = true;
        vc2.powertrain.wheelGroups[1].trackWidth = 1.6f;
        w2[0].TestAttach(new CamberController());
        UnityEngine.Object.Registry.Add(vc2);
        var t2 = new VehicleTuner();
        AlignmentSettings.Enabled = true;
        AlignmentSettings.SetPresetByName("Race");
        t2.ReapplyNow();
        Check(Near(w2[0].Camber, -1.2f) && !Near(w2[1].Camber, -1.2f), "CamberController wheel keeps its camber; its neighbour is tuned");
        Check(Near(w2[2].Camber, -0.6f) && Near(w2[3].Camber, -0.6f), "solid-axle wheels keep their camber (WheelGroup.Update owns it)");
        Check(t2.AnyCamberLocked && t2.ReferenceWheelStock(WheelRole.FL).HasCamberController, "camber-locked wheels flagged for the panel warning");
        Check(t2.AnyWheelsMoved, "moved wheels flagged (stale wheelbase/track width warning)");
        AlignmentSettings.Enabled = false;
        t2.ApplyLive();

        // Baseline refresh on OFF -> ON: a camber the game changed while OFF is the new stock.
        w2[1].Camber = -2f;
        AlignmentSettings.Enabled = true;
        AlignmentSettings.SetPresetByName("Street");
        t2.ApplyLive();
        Check(Near(w2[1].Camber, -2f + AlignmentSettings.ActivePreset.CamberFR), "OFF->ON re-reads the stock geometry");
        AlignmentSettings.Enabled = false;
        t2.ApplyLive();
        Check(Near(w2[1].Camber, -2f), "and restores to it");

        // Camber beyond the setter's +-16 clamp continues through the transform roll
        // (euler Z), matching UpdateWheelValues' side sign; euler X/Y stay untouched.
        AlignmentSettings.Enabled = true;
        AlignmentPreset big = AlignmentSettings.BeginEdit();
        big.SetCamber(WheelRole.FR, 25f);
        float stockCam = t2.ReferenceWheelStock(WheelRole.FR).Camber;   // refreshed earlier: -2
        float baseZ = w2[1].transform.localEulerAngles.z;
        t2.ApplyLive();
        Check(Near(w2[1].Camber, 16f), "camber clamped at the engine's +16 in the setter");
        Check(Near(w2[1].transform.localEulerAngles.z, baseZ + (25f + stockCam - 16f) * -1f),
            "the excess continues as a transform roll (euler Z, right side -1)");
        Check(Near(w2[1].transform.localEulerAngles.x, -big.CasterF) && Near(w2[1].transform.localEulerAngles.y, -big.ToeF),
            "euler X/Y keep exactly the group's caster/toe values (the overflow only rolls Z)");
        AlignmentSettings.Enabled = false;
        t2.ApplyLive();
        Check(Near(w2[1].Camber, stockCam) && Near(w2[1].transform.localEulerAngles.z, baseZ),
            "OFF restores camber and the roll exactly");
        ResetAllCategories();

        // Telemetry: the driven vehicle is picked by live FSM input, not
        // Vehicle.ActiveVehicle (the game never sets isPlayerControllable).
        UnityEngine.Object.Registry.Clear();
        VehicleController idle = MakeCar(out FakeWheel[] _, 0f);
        VehicleController driving = MakeCar(out FakeWheel[] _, 0f);
        idle.Speed = 3f;
        driving.Speed = 25f;
        driving.input.Steering = 0.6f;
        driving.input.Throttle = 0.8f;
        UnityEngine.Object.Registry.Add(idle);
        UnityEngine.Object.Registry.Add(driving);
        var t3 = new VehicleTuner();
        t3.ReapplyNow();
        VehicleTuner.TelemetrySample ts;
        Check(t3.TryGetTelemetry(out ts) && Near(ts.SpeedKmh, 90f),
            "telemetry reads the driven car (most live input), not the first tracked");
        // Input stops and the engine stalls: the last driven car stays picked.
        driving.input.Steering = 0f;
        driving.input.Throttle = 0f;
        driving.Speed = 0f;
        driving.powertrain.engine.OutputRPM = 0f;
        Check(t3.TryGetTelemetry(out ts) && Near(ts.SpeedKmh, 0f),
            "no input + stalled engine: the last driven car stays picked (not an NPC fallback)");
        UnityEngine.Object.Registry.Clear();
        var t4 = new VehicleTuner();
        UnityEngine.Object.Registry.Add(idle);
        t4.ReapplyNow();
        Check(t4.TryGetTelemetry(out ts) && Near(ts.SpeedKmh, 10.8f), "no input anywhere: fastest car wins");
        // Parked, no input: the running engine beats a dead one (the player's car idles).
        VehicleController parked = MakeCar(out FakeWheel[] _, 0f);
        parked.powertrain.engine.OutputRPM = 800f;
        parked.Speed = 0f;
        idle.Speed = 0f;
        UnityEngine.Object.Registry.Clear();
        UnityEngine.Object.Registry.Add(idle);
        UnityEngine.Object.Registry.Add(parked);
        var t5 = new VehicleTuner();
        t5.ReapplyNow();
        Check(t5.TryGetTelemetry(out ts) && Near(ts.Rpm, 800f), "parked with a running engine beats a parked dead engine");
        ResetAllCategories();
    }

    private static void TestTargeting()
    {
        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        TargetSettings.Mode = TargetMode.All;
        TargetSettings.SelectedName = "";

        VehicleController a = MakeCar(out FakeWheel[] wa, 0f);
        VehicleController b = MakeCar(out FakeWheel[] wb, 0f);
        a.gameObject.name = "Rustallion(Clone)";
        b.gameObject.name = "Junker(Clone)";
        UnityEngine.Object.Registry.Add(a);
        UnityEngine.Object.Registry.Add(b);
        var tuner = new VehicleTuner();
        tuner.ReapplyNow();
        SuspensionSettings.Enabled = true;
        SuspensionSettings.SetPresetByName("Race");

        // Selected: only the named vehicle is tuned.
        TargetSettings.Mode = TargetMode.Selected;
        TargetSettings.SelectedName = "Rustallion(Clone)";
        tuner.ApplyLive();
        float raceSpring = wa[0].SpringMaxForce;
        Check(!Near(raceSpring, 30000f) && Near(wb[0].SpringMaxForce, 30000f),
            "Selected mode tunes only the named vehicle");
        Check(tuner.TargetedCount == 1, "TargetedCount = 1 in Selected mode");

        // Switching the selection restores A and tunes B.
        TargetSettings.SelectedName = "Junker(Clone)";
        tuner.ApplyLive();
        Check(Near(wa[0].SpringMaxForce, 30000f) && !Near(wb[0].SpringMaxForce, 30000f),
            "switching the selection restores the old vehicle and tunes the new one");

        // OFF restores only what was applied (B), leaving A's own stock intact.
        SuspensionSettings.Enabled = false;
        tuner.ApplyLive();
        Check(Near(wb[0].SpringMaxForce, 30000f) && Near(wa[0].SpringMaxForce, 30000f),
            "OFF restores the applied record only");

        // Last driven follows live FSM input.
        SuspensionSettings.Enabled = true;
        TargetSettings.Mode = TargetMode.LastDriven;
        a.input.Throttle = 1f;
        tuner.ApplyLive();
        Check(!Near(wa[0].SpringMaxForce, 30000f) && Near(wb[0].SpringMaxForce, 30000f),
            "Last driven tunes the vehicle with live input");
        a.input.Throttle = 0f;
        b.input.Brakes = 1f;
        tuner.ApplyLive();
        Check(Near(wa[0].SpringMaxForce, 30000f) && !Near(wb[0].SpringMaxForce, 30000f),
            "the driven pick follows the input as it moves between vehicles");
        b.input.Brakes = 0f;
        SuspensionSettings.Enabled = false;
        tuner.ApplyLive();

        // All covers everything again.
        SuspensionSettings.Enabled = true;
        TargetSettings.Mode = TargetMode.All;
        tuner.ApplyLive();
        Check(!Near(wa[0].SpringMaxForce, 30000f) && !Near(wb[0].SpringMaxForce, 30000f)
              && tuner.TargetedCount == 2, "All mode tunes every tracked vehicle");
        SuspensionSettings.Enabled = false;
        tuner.ApplyLive();
        Check(Near(wa[0].SpringMaxForce, 30000f) && Near(wb[0].SpringMaxForce, 30000f), "All-mode OFF restores both");

        // Parse fallback + config round-trip.
        Check(TargetSettings.Parse("Selected Vehicle") == TargetMode.Selected
              && TargetSettings.Parse("lastdriven") == TargetMode.LastDriven
              && TargetSettings.Parse("bogus") == TargetMode.All && TargetSettings.Parse("") == TargetMode.All,
            "mode parse: name-only, case tolerant, garbage falls back to All");
        string path = Path.Combine(Path.GetTempPath(), "target-" + Guid.NewGuid().ToString("N") + ".cfg");
        ModConfig.Load(new ConfigFile(path, true));
        TargetSettings.Mode = TargetMode.Selected;
        TargetSettings.SelectedName = "Junker(Clone)";
        ModConfig.Save();
        TargetSettings.Mode = TargetMode.All;
        TargetSettings.SelectedName = "";
        ModConfig.Load(new ConfigFile(path, true));
        Check(TargetSettings.Mode == TargetMode.Selected && TargetSettings.SelectedName == "Junker(Clone)",
            "ApplyTarget/SelectedVehicle persist through the config file");
        try { File.Delete(path); } catch { }
        ResetAllCategories();
    }

    private static void TestGearbox()
    {
        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        TransmissionComponent.DeferShifts = false;

        float[] stock = { -2.216f, 0f, 3.274f, 2.093f, 1.439f, 1.084f, 0.817f };
        int rev, fwd;
        Check(VehicleTuner.AnalyseLayout(stock, out rev, out fwd) && rev == 1 && fwd == 5, "layout: 1 reverse, neutral, 5 forward");
        Check(!VehicleTuner.AnalyseLayout(new[] { 3f, 0f, -2f }, out rev, out fwd), "non-standard layout refused (never touched)");
        float[] ext = VehicleTuner.ExtendRatios(stock, 1, 5);
        Check(ext.Length == 12 && Near(ext[4], 0.817f) && Near(ext[5], 0.817f * 0.817f / 1.084f), "6th gear continues geometrically: r5^2 / r4");
        Check(ext[11] >= VehicleTuner.MinContinuedRatio && ext[11] < ext[10], "continuation keeps falling, never below 0.05");
        Check(Near(VehicleTuner.ExtendRatios(new[] { -2f, 0f, 3f }, 1, 1)[1], 2.25f), "one forward gear: continues at x0.75");

        VehicleController vc = MakeCar(out FakeWheel[] _, 0f);
        TransmissionComponent t = vc.powertrain.transmission;
        ClutchComponent cl = vc.powertrain.clutch;
        UnityEngine.Object.Registry.Add(vc);
        var tuner = new VehicleTuner();
        tuner.ReapplyNow();
        Check(tuner.ReferenceGearCount == 5 && Near(tuner.ReferenceGearStock(6), ext[5]), "reference vehicle: 5 gears, continued 6th for the readouts");

        // 0.7.0: automatics are tunable — the mod's shift controller owns the box while ON
        // (0.6.x skipped them entirely; ShiftController tests cover the shifting itself).
        GearboxSettings.Enabled = true;
        GearboxPreset autoP = GearboxSettings.BeginEdit();
        autoP.GearCount = 6;
        tuner.ApplyLive();
        Check(t.gears.Count == 8 && tuner.ShiftControlledCount == 1 && t.transmissionType == TransmissionComponent.TransmissionShiftType.Automatic,
            "automatic transmission: resized to 6 gears and shifted by the mod (its type is never written)");
        GearboxSettings.Enabled = false;
        tuner.ApplyLive();
        Check(t.gears.Count == 7 && t.HasNwhDelegate && tuner.ShiftControlledCount == 0, "OFF: stock list and NWH's own delegate back");

        // 5 -> 6 gears (manual transmission opts in).
        t.transmissionType = TransmissionComponent.TransmissionShiftType.Manual;
        GearboxSettings.Enabled = true;
        GearboxPreset p = GearboxSettings.BeginEdit();
        p.GearCount = 6;
        p.SetScale(1, 1.2f);
        tuner.ApplyLive();
        Check(t.gears.Count == 8 && Near(t.gears[0], -2.216f) && t.gears[1] == 0f, "6 gears: reverse and neutral untouched");
        Check(Near(t.gears[2], 3.274f * 1.2f) && Near(t.gears[7], ext[5]), "1st gear factor applied; 6th = continued ratio");
        Check(Near(t.finalGearRatio, 6f) && Near(t.shiftDuration, 0.2f) && Near(t.UpshiftRPM, 2800f),
            "no field shared with Drivetrain (final drive, shift time, shift RPMs untouched)");

        // 6 -> 4 while in 5th, post-shift ban active: ShiftInto would refuse, the guard must not.
        t.Gear = 5;
        t.isPostShiftBanActive = true;
        t.ShiftInto(4, true);
        Check(t.Gear == 5, "(stub mirrors NWH) ShiftInto(4, instant) refuses during the post-shift ban — the spec's recipe");
        p.GearCount = 4;
        tuner.ApplyLive();
        bool threw = false;
        try { t.SimulateForwardStep(); } catch (Exception) { threw = true; }
        Check(t.gears.Count == 6 && t.Gear == 4 && !threw, "cut to 4 gears: current gear pulled to 4 at once, no out-of-range ratio");
        t.isPostShiftBanActive = false;

        // A shift in flight to a gear that a shrink would remove: the shrink waits.
        p.GearCount = 6;
        tuner.ApplyLive();
        t.Gear = 4;
        TransmissionComponent.DeferShifts = true;
        t.ShiftInto(5);
        Check(t.isShifting && t.Gear == 4, "shift 4 -> 5 in flight");
        p.GearCount = 4;
        tuner.ApplyLive();
        Check(t.gears.Count == 8, "shrink deferred while the shift is in flight");
        t.CompletePendingShift();
        tuner.ApplyLive();
        threw = false;
        try { t.SimulateForwardStep(); } catch (Exception) { threw = true; }
        Check(t.gears.Count == 6 && t.Gear == 4 && !threw, "shrink lands after the shift; gear clamped; no exception");

        // Restore during a shift to a tuned-only gear: placeholders until it lands, then trimmed.
        p.GearCount = 7;
        tuner.ApplyLive();
        t.Gear = 6;
        t.ShiftInto(7);
        GearboxSettings.Enabled = false;
        tuner.ApplyLive();
        t.CompletePendingShift();
        threw = false;
        try { t.SimulateForwardStep(); } catch (Exception) { threw = true; }
        Check(!threw && t.Gear == 7 && Near(t.gears[t.gearIndex], 0.817f), "restore mid-shift keeps placeholder gears (stock top ratio): no exception");
        tuner.ApplyLive();
        Check(t.gears.Count == 7 && t.Gear == 5, "placeholders trimmed after the shift; gear clamped to the stock top gear");
        TransmissionComponent.DeferShifts = false;
        bool same = t.gears.Count == stock.Length;
        for (int i = 0; same && i < stock.Length; i++) same &= Near(t.gears[i], stock[i]);
        Check(same, "gear list restored exactly");

        // Clutch types + clamps + mode.
        GearboxSettings.Enabled = true;
        GearboxSettings.SetPresetByName("Race");
        tuner.ApplyLive();
        Check(Near(cl.slipTorque, 500f * 1.8f) && Near(cl.engagementRange, 400f * 0.6f) && Near(cl.engagementRPM, 1000f),
            "Race clutch: capacity x1.8, range x0.6, engagement -200 rpm");
        Check(GearboxSettings.ClutchTypeIndex(GearboxSettings.Shown) == 3, "Race preset matches the Race clutch type");
        Check(Near(VehicleTuner.EngagementRpm(1200f, -500f, 900f), 990f), "engagement never pushed below 1.1 x idle");
        Check(Near(VehicleTuner.EngagementRpm(800f, -100f, 900f), 800f), "... unless the stock point is already below it (kept as shipped)");
        p = GearboxSettings.BeginEdit();
        p.TransmissionMode = GearboxMode.Automatic;
        tuner.ApplyLive();
        Check(t.transmissionType == TransmissionComponent.TransmissionShiftType.Manual && !t.HasNwhDelegate,
            "0.7.0: transmission mode Automatic is the controller's logic; the vehicle's type is never written");
        GearboxSettings.Enabled = false;
        tuner.ApplyLive();
        Check(Near(cl.slipTorque, 500f) && Near(cl.engagementRange, 400f) && Near(cl.engagementRPM, 1200f)
              && t.transmissionType == TransmissionComponent.TransmissionShiftType.Manual && t.HasNwhDelegate,
            "OFF restores clutch; the type is still the vehicle's own Manual with NWH's delegate");

        // Clamp: slip torque >= 1, range >= 1 even from a zero stock value.
        cl.slipTorque = 0f;
        cl.engagementRange = 0f;
        GearboxSettings.Enabled = true;
        tuner.ApplyLive();
        Check(cl.slipTorque >= 1f && cl.engagementRange >= 1f, "slipTorque and engagementRange clamped >= 1 (no divide-by-zero)");
        GearboxSettings.Enabled = false;
        tuner.ApplyLive();

        // CVT: gears and mode refused, clutch still applies.
        UnityEngine.Object.Registry.Clear();
        VehicleController cvt = MakeCar(out FakeWheel[] _, 0f);
        cvt.powertrain.transmission.gears = new List<float> { -3f, 0f, 2f };
        cvt.powertrain.transmission.transmissionType = TransmissionComponent.TransmissionShiftType.CVT;
        UnityEngine.Object.Registry.Add(cvt);
        var t3 = new VehicleTuner();
        GearboxSettings.Enabled = true;
        p = GearboxSettings.BeginEdit();
        p.GearCount = 6;
        p.SetScale(1, 1.4f);
        p.ClutchGripScale = 1.5f;
        p.TransmissionMode = GearboxMode.Manual;
        t3.ReapplyNow();
        Check(cvt.powertrain.transmission.gears.Count == 3 && Near(cvt.powertrain.transmission.gears[2], 2f),
            "CVT: gear count and ratios never touched (VC_Validate needs exactly 3)");
        Check(cvt.powertrain.transmission.transmissionType == TransmissionComponent.TransmissionShiftType.CVT, "CVT keeps its mode");
        Check(Near(cvt.powertrain.clutch.slipTorque, 750f) && t3.AnyCvt && t3.ReferenceIsCvt, "CVT: clutch still applies; flagged for the panel");
        GearboxSettings.Enabled = false;
        t3.ApplyLive();

        // Self-heal (0.6.1): a save made while tuned bakes the continuation into the vehicle.
        float[] ext12 = VehicleTuner.ExtendRatios(stock, 1, 5);   // 12 forward ratios: 5 stock + 7 continuation
        List<float> poisonedList = new List<float> { stock[0], 0f };
        poisonedList.AddRange(ext12);
        Check(VehicleTuner.TryStripContinuation(poisonedList, 1) == 5,
            "a 12-gear poisoned list strips back to the 5 real gears (the first continuation gear is exact by construction, hence +1)");
        Check(VehicleTuner.TryStripContinuation(new List<float>(stock), 1) == 0,
            "a stock 5-gear list has nothing to strip (its near-geometric top is not exact enough)");
        HutongGames.PlayMaker.FsmStateAction[] shiftActions =
        {
            new HutongGames.PlayMaker.Actions.GetButtonDown { buttonName = new HutongGames.PlayMaker.FsmString("ShiftInto1") },
            new HutongGames.PlayMaker.Actions.GetButtonDown { buttonName = new HutongGames.PlayMaker.FsmString("ShiftInto5") },
            new HutongGames.PlayMaker.Actions.GetButtonDown { buttonName = new HutongGames.PlayMaker.FsmString("ShiftIntoR1") },
            new HutongGames.PlayMaker.Actions.GetButtonDown { buttonName = new HutongGames.PlayMaker.FsmString("Horn") }
        };
        Check(VehicleTuner.CountShiftIntos(shiftActions) == 5, "the game's ShiftIntoN actions give the stock gear count (reverse and others ignored)");
        VehicleController poisoned = MakeCar(out FakeWheel[] _, 0f);
        poisoned.powertrain.transmission.gears = new List<float>(poisonedList);
        poisoned.powertrain.transmission.Gear = 9;
        UnityEngine.Object.Registry.Add(poisoned);
        var tp = new VehicleTuner();
        tp.ReapplyNow();
        Check(poisoned.powertrain.transmission.gears.Count == 7 && poisoned.powertrain.transmission.Gear <= 5,
            "capture truncates a baked-in continuation and fixes the live gear");
        ResetAllCategories();
    }

    private static void TestInputBlocker()
    {
        bool allDriving = true;
        foreach (string k in InputBlocker.DrivingKeys) allDriving &= InputBlocker.RouteControllerAction(k) == InputBlocker.Route.Allow;
        foreach (string k in InputBlocker.DrivingAxes) allDriving &= InputBlocker.RouteControllerAction(k) == InputBlocker.Route.Allow;
        Check(allDriving, "every whitelisted driving key/axis passes (Throttle ... Cruise Control, input, Steering Keyboard)");
        Check(InputBlocker.RouteControllerAction("Mouse X") == InputBlocker.Route.Block
              && InputBlocker.RouteControllerAction("Pause") == InputBlocker.Route.Block
              && InputBlocker.RouteControllerAction(null) == InputBlocker.Route.Block
              && InputBlocker.RouteControllerAction("") == InputBlocker.Route.Block,
            "unknown / mouse / null names are blocked (fail-closed; never reach InputStorage, which throws)");
        Check(InputBlocker.RouteControllerAction("throttle") == InputBlocker.Route.Block, "names are exact (ordinal), like InputStorage's lookup");

        Check(InputBlocker.RouteFsmAction("GetButton", "Throttle") == InputBlocker.Route.Allow, "GetButton fork with a driving name runs (layer B defers to the whitelist)");
        Check(InputBlocker.RouteFsmAction("GetAxis", "input") == InputBlocker.Route.Allow, "GetAxis fork with the steering axis runs");
        Check(InputBlocker.RouteFsmAction("GetButton", "Inventory") == InputBlocker.Route.Block, "GetButton fork with a non-driving name is blocked");
        Check(InputBlocker.RouteFsmAction("GetAxisOrig", "Mouse X") == InputBlocker.Route.Block, "GetAxisOrig (direct Input: mouse look) blocked");
        Check(InputBlocker.RouteFsmAction("AnyKey", null) == InputBlocker.Route.Block
              && InputBlocker.RouteFsmAction("MouseLook", null) == InputBlocker.Route.Block
              && InputBlocker.RouteFsmAction("GetKeyDown", null) == InputBlocker.Route.Block, "AnyKey / MouseLook / direct GetKey* blocked");

        Check(InputBlocker.RouteOnEnter(InputBlocker.Route.Allow, true, false) == InputBlocker.EnterRoute.Run, "OnEnter of an allowed action runs");
        Check(InputBlocker.RouteOnEnter(InputBlocker.Route.Block, true, false) == InputBlocker.EnterRoute.SkipAndFinish,
            "blocked one-shot OnEnter is skipped AND finished (the FSM state never hangs)");
        Check(InputBlocker.RouteOnEnter(InputBlocker.Route.Block, true, true) == InputBlocker.EnterRoute.Skip, "blocked every-frame OnEnter is skipped");
        Check(InputBlocker.RouteOnEnter(InputBlocker.Route.Block, false, false) == InputBlocker.EnterRoute.Run,
            "OnEnter without an everyFrame flag runs (cannot be skipped safely)");

        var drive = new GetButton { buttonName = new FsmString("Throttle") };
        var inv = new GetButton { buttonName = new FsmString("Inventory"), everyFrame = false };
        var look = new GetAxisOrig { axisName = new FsmString("Mouse X"), everyFrame = true };
        Check(InputBlocker.RouteInstance(drive) == InputBlocker.Route.Allow && InputBlocker.RouteInstance(look) == InputBlocker.Route.Block,
            "live instances: the name field is read through reflection");
        Check(InputBlocker.RouteEnterInstance(inv) == InputBlocker.EnterRoute.SkipAndFinish
              && InputBlocker.RouteEnterInstance(new MouseLook()) == InputBlocker.EnterRoute.Skip
              && InputBlocker.RouteEnterInstance(new AnyKey()) == InputBlocker.EnterRoute.Run, "OnEnter decisions on live instances");
        Check(InputBlocker.LoggedNameCount >= 2, "blocked unknown names are recorded for the discovery log");

        // Freeze split: SetInputBlocked never touches timeScale; SetFreeze saves/restores exactly.
        Time.timeScale = 1f;
        InputBlocker.SetInputBlocked(true);
        Check(Near(Time.timeScale, 1f), "live mode: blocking input leaves timeScale alone (the game keeps running)");
        InputBlocker.SetFreeze(true);
        Check(Near(Time.timeScale, 0f) && InputBlocker.Frozen, "Freeze ON sets timeScale 0");
        InputBlocker.SetFreeze(false);
        Check(Near(Time.timeScale, 1f), "unfreeze restores 1");
        Time.timeScale = 0f;   // opened from the pause menu
        InputBlocker.SetFreeze(true);
        InputBlocker.SetFreeze(false);
        Check(Near(Time.timeScale, 0f), "a pause-menu 0 is restored as 0 (never unpaused)");
        InputBlocker.SetInputBlocked(false);
        Time.timeScale = 1f;
    }

    private static void FillDistinct(PresetCategory c)
    {
        // Non-default, in-range values for every key of the category's Custom slot.
        switch (c)
        {
            case PresetCategory.Steering:
                SteeringPreset s = SteeringPreset.Custom;
                s.RateMultiplier = 1.37f; s.SmoothingScale = 0.83f; s.UseVehicleCurve = false;
                s.LockCurve = EditableCurve.FromPoints(0f, 1f, 0.3f, 0.5f, 1f, 0.2f);
                s.ReturnCurve = EditableCurve.FromPoints(0f, 0f, 1f, 0.7f);
                s.TractionClampEnabled = false; s.SlipAngleDeg = 6.25f; s.OppositeLockBoost = 1.1f;
                s.LinearityOverride = true; s.LinearityExponent = 1.35f; s.BasedOn = "Drift";
                break;
            case PresetCategory.Alignment:
                AlignmentPreset a = AlignmentPreset.Custom;
                for (int r = 0; r < 4; r++)
                {
                    a.SetCamber((WheelRole)r, -1.5f - r);
                    for (int ax = 0; ax < 3; ax++) a.SetPos((WheelRole)r, ax, r * 3 + ax - 5.5f);
                }
                a.CasterF = 4.5f; a.CasterR = -1f; a.ToeF = 0.12f; a.ToeR = -0.07f; a.BasedOn = "Race";
                break;
            case PresetCategory.Gearbox:
                GearboxPreset g = GearboxPreset.Custom;
                g.GearCount = 7;
                for (int i = 1; i <= 12; i++) g.SetScale(i, 0.6f + i * 0.07f);
                g.ClutchGripScale = 1.3f; g.ClutchRangeScale = 0.7f; g.ClutchRpmOffset = -150f; g.TransmissionMode = GearboxMode.Manual;
                break;
            case PresetCategory.Weight:
                WeightPreset.Custom.FrontKg = 420f; WeightPreset.Custom.RearKg = -260f; WeightPreset.Custom.BasedOn = "Front ballast";
                break;
            case PresetCategory.Suspension:
                SuspensionPreset.Custom.CopyFrom(SuspensionSettings.Book.FindBuiltIn("Race"));
                SuspensionPreset.Custom.SpringRear = 2.75f; SuspensionPreset.Custom.BasedOn = "Race";
                break;
            case PresetCategory.Drivetrain:
                DrivetrainPreset.Custom.CopyValuesFrom(DrivetrainSettings.Book.FindBuiltIn("Drift"));
                DrivetrainPreset.Custom.PowerScale = 3.1f; DrivetrainPreset.Custom.BasedOn = "Drift";
                break;
            case PresetCategory.Assists:
                AssistsPreset.Custom.CopyValuesFrom(AssistsSettings.Book.FindBuiltIn("Sport"));
                AssistsPreset.Custom.TcsCutoffSpeed = 3.3f;
                break;
            case PresetCategory.Aero:
                AeroPreset.Custom.DownforceScale = 1.7f; AeroPreset.Custom.DragScale = 0.4f; AeroPreset.Custom.MaxDownforceSpeedScale = 1.9f;
                break;
            case PresetCategory.Brakes:
                BrakesPreset.Custom.TorqueScale = 2.6f; BrakesPreset.Custom.HandbrakeScale = 1.8f; BrakesPreset.Custom.ActuationScale = 0.6f;
                break;
            case PresetCategory.Grip:
                GripPreset.Custom.LongitudinalScale = 0.15f; GripPreset.Custom.LateralScale = 2.9f; GripPreset.Custom.StiffnessScale = 1.01f;
                break;
        }
    }

    private static ITunablePreset CustomOf(PresetCategory c)
    {
        switch (c)
        {
            case PresetCategory.Steering: return SteeringPreset.Custom;
            case PresetCategory.Suspension: return SuspensionPreset.Custom;
            case PresetCategory.Aero: return AeroPreset.Custom;
            case PresetCategory.Brakes: return BrakesPreset.Custom;
            case PresetCategory.Grip: return GripPreset.Custom;
            case PresetCategory.Drivetrain: return DrivetrainPreset.Custom;
            case PresetCategory.Assists: return AssistsPreset.Custom;
            case PresetCategory.Alignment: return AlignmentPreset.Custom;
            case PresetCategory.Gearbox: return GearboxPreset.Custom;
            default: return WeightPreset.Custom;
        }
    }

    private static void TestPresetCodec()
    {
        ResetAllCategories();
        bool allRoundTrip = true;
        foreach (PresetCategory c in Enum.GetValues(typeof(PresetCategory)))
        {
            FillDistinct(c);
            string a = PresetCodec.Serialize(c, CustomOf(c));
            ResetAllCategories();
            PresetCodec.Result r = PresetCodec.Import(c, a);
            string b = PresetCodec.Serialize(c, CustomOf(c));
            bool ok = r.Ok && a == b && r.Unknown == 0 && r.Applied == PresetCodec.KeyCount(c);
            if (!ok) Console.WriteLine("    " + c + ":\n      " + a + "\n      " + b);
            allRoundTrip &= ok;
            ResetAllCategories();
        }
        Check(allRoundTrip, "Serialize -> Import -> Serialize is byte-identical for all ten books");
        Check(PresetCodec.KeyCount(PresetCategory.Alignment) == 20 && PresetCodec.KeyCount(PresetCategory.Gearbox) == 21,
            "alignment carries 4 camber + caster/toe + 12 position keys; gearbox 12 gear + 9 keys (0.7.0: + Spread/ShiftUp/ShiftDown/Kickdown)");

        // A built-in exports with BasedOn = its name; import forks Custom(Name), the built-in stays.
        SuspensionPreset race = SuspensionSettings.Book.FindBuiltIn("Race");
        float raceSpring = race.SpringFront;
        string text = PresetCodec.Serialize(PresetCategory.Suspension, race);
        Check(text.StartsWith("AVT1|Suspension|Race|BasedOn=Race|"), "header: tag, category, preset name, BasedOn");
        PresetCodec.Result imp = PresetCodec.Import(PresetCategory.Suspension, text);
        Check(imp.Ok && SuspensionSettings.ActivePreset == SuspensionPreset.Custom && SuspensionPreset.Custom.BasedOn == "Race"
              && Near(SuspensionPreset.Custom.SpringRear, race.SpringRear) && Near(race.SpringFront, raceSpring),
            "pasting a built-in lands in Custom (Race), the built-in itself untouched");

        // Garbage and wrong tabs.
        Check(PresetCodec.Import(PresetCategory.Suspension, "").Why == PresetCodec.Failure.Empty, "empty clipboard rejected");
        Check(PresetCodec.Import(PresetCategory.Suspension, "hello world").Why == PresetCodec.Failure.WrongTag, "random text rejected");
        Check(PresetCodec.Import(PresetCategory.Suspension, "AVT9|Suspension|X|SpringFront=1").Why == PresetCodec.Failure.WrongTag, "unknown format version rejected");
        PresetCodec.Result wrong = PresetCodec.Import(PresetCategory.Suspension, "AVT1|Aero|Race|BasedOn=Race|DragScale=1.3");
        Check(!wrong.Ok && wrong.Why == PresetCodec.Failure.WrongCategory && wrong.Category == "Aero", "a preset of another tab is rejected");
        Check(PresetCodec.Import(PresetCategory.Suspension, "AVT1|Suspension|Custom|SpringFront").Why == PresetCodec.Failure.Malformed, "a field without '=' is malformed");

        // Clamp, unknown keys, missing keys, unresolved BasedOn.
        SuspensionSettings.ResetAll();
        SuspensionPreset.Custom.ArbRear = 1.33f;
        PresetCodec.Result cl = PresetCodec.Import(PresetCategory.Suspension, "AVT1|Suspension|Custom|BasedOn=Nope|SpringFront=99|Warp=3|BumpRear=abc");
        Check(cl.Ok && Near(SuspensionPreset.Custom.SpringFront, Limits.SuspFactorMax) && cl.Clamped == 1, "out-of-range value clamped to Limits");
        Check(cl.Unknown == 2 && cl.Applied == 1, "unknown keys and unreadable values skipped and counted");
        Check(Near(SuspensionPreset.Custom.ArbRear, 1.33f) && Near(SuspensionPreset.Custom.BumpRear, 1f), "missing keys keep Custom's current value");
        Check(SuspensionPreset.Custom.BasedOn == "", "BasedOn that names no built-in becomes \"\"");
        Check(SettingsPanel.PasteStatus(cl).Contains("1 values applied") && SettingsPanel.PasteStatus(cl).Contains("2 unknown"),
            "paste status line reports applied and skipped counts");
        Check(SettingsPanel.PasteStatus(wrong).Contains(UiStrings.PasteReasonWrongCategory), "paste status names the failure");
        ResetAllCategories();
    }

    private static AcceptableValueRange<float> RangeOf(ConfigFile cfg, string section, string key)
    {
        return cfg[new ConfigDefinition(section, key)].Description.AcceptableValues as AcceptableValueRange<float>;
    }

    private static void TestConfig060(string dir)
    {
        ResetAllCategories();
        string fresh = Path.Combine(dir, "fresh060.cfg");
        var cfg = new ConfigFile(fresh, true);
        ModConfig.Load(cfg);
        Check(!UiSettings.FreezeWhileOpen && Near(UiSettings.PanelWidth, 460f) && Near(UiSettings.PanelScale, 1f)
              && Near(UiSettings.PanelAlpha, 1f) && UiSettings.LastTab == 0, "UI defaults: live panel, 460 px, scale 1, opaque, first tab");
        Check(!UiSettings.TelemetryEnabled && Near(UiSettings.TelemetryScale, 1f) && UiSettings.TelemetryPosition == TelemetryCorner.TopLeft,
            "telemetry defaults: off (opt-in), x1, top left");
        Check(!AlignmentSettings.Enabled && !GearboxSettings.Enabled && AlignmentSettings.ActivePreset == AlignmentPreset.Stock
              && GearboxSettings.ActivePreset == GearboxPreset.Stock, "Alignment and Gearbox are opt-in (off, Stock)");

        // Limits drive the AcceptableValueRanges.
        Func<AcceptableValueRange<float>, float, float, bool> is_ = (r, lo, hi) => r != null && Near(r.MinValue, lo) && Near(r.MaxValue, hi);
        Check(is_(RangeOf(cfg, "Suspension.Custom", "SpringFront"), Limits.SuspFactorMin, Limits.SuspFactorMax)
              && is_(RangeOf(cfg, "Drivetrain.Custom", "PowerScale"), Limits.PowerMin, Limits.PowerMax)
              && is_(RangeOf(cfg, "Drivetrain.Custom", "FinalDriveScale"), Limits.FinalDriveMin, Limits.FinalDriveMax)
              && is_(RangeOf(cfg, "Drivetrain.Custom", "LossScale"), Limits.LossMin, Limits.LossMax)
              && is_(RangeOf(cfg, "Drivetrain.Custom", "BoostScale"), Limits.BoostMin, Limits.BoostMax)
              && is_(RangeOf(cfg, "Grip.Custom", "LateralScale"), Limits.GripMin, Limits.GripMax)
              && is_(RangeOf(cfg, "Brakes.Custom", "TorqueScale"), Limits.BrakeTorqueMin, Limits.BrakeTorqueMax),
            "widened ranges follow Limits (suspension 0.1-5, power 0.1-5, final drive 0.25-3, loss/boost 0-4, grip 0.05-5, brake torque 0.1-5)");
        Check(is_(RangeOf(cfg, "Drivetrain.Custom", "UpshiftScale"), 0.8f, 1.2f),
            "NOT widened: shift RPMs (lock-up guard)");
        Check(is_(RangeOf(cfg, "Alignment.Custom", "CamberFL"), -45f, 45f) && is_(RangeOf(cfg, "Alignment.Custom", "CasterFront"), -25f, 25f)
              && is_(RangeOf(cfg, "Alignment.Custom", "ToeRear"), -15f, 15f) && is_(RangeOf(cfg, "Alignment.Custom", "PosZRR"), -100f, 100f)
              && is_(RangeOf(cfg, "Gearbox.Custom", "Gear12Scale"), 0.25f, 3f) && is_(RangeOf(cfg, "UI", "PanelWidth"), 300f, 1000f),
            "new keys carry their Limits ranges");
        var gcRange = cfg[new ConfigDefinition("Gearbox.Custom", "GearCount")].Description.AcceptableValues as AcceptableValueRange<int>;
        Check(gcRange != null && gcRange.MinValue == 0 && gcRange.MaxValue == 12, "GearCount is an int range 0..12");

        // A 0.5.0 file loads as-is: every old key and value kept, new sections added.
        string old = Path.Combine(dir, "v050.cfg");
        File.WriteAllText(old,
            "[General]\nApocasetter = true\n\n[Steering]\nEnabled = true\nPreset = Euro Truck\nMatchGameSteeringSpeed = false\n\n" +
            "[Steering.Custom]\nBasedOn = Drift\nRateMultiplier = 1.5\nSmoothingScale = 1\nUseVehicleCurve = false\nLockCurve = 0:1;0.5:0.4;1:0.2\n" +
            "ReturnCurve = 0:0;1:0.7\nTractionClampEnabled = true\nSlipAngleDeg = 7\nOppositeLockBoost = 1.5\nLinearityOverride = false\nLinearityExponent = 1\n\n" +
            "[Suspension]\nEnabled = true\nPreset = Custom\nSplitFrontRear = true\n\n[Suspension.Custom]\nBasedOn = Sport\nSpringFront = 0.5\nSpringRear = 2\n\n" +
            "[Aero]\nEnabled = false\nPreset = Race\n\n[Brakes]\nEnabled = true\nPreset = Drift\n\n[Brakes.Custom]\nActuationScale = 1.9\n\n" +
            "[Grip]\nEnabled = false\nPreset = Sport\n\n[Drivetrain]\nEnabled = true\nPreset = Custom\n\n[Drivetrain.Custom]\nPowerScale = 2.5\nDiffFrontMode = LimitedSlip\n\n" +
            "[Assists]\nEnabled = true\nPreset = Standard\n\n[UI]\nToggleKey = F8\n");
        ModConfig.Load(new ConfigFile(old, true));
        Check(SteeringSettings.Enabled && SteeringSettings.ActivePreset == SteeringPreset.FindBuiltIn("Euro Truck") && !SteeringSettings.MatchGameSteeringSpeed,
            "0.5.0 cfg: steering state kept");
        Check(SuspensionSettings.Enabled && SuspensionSettings.ActivePreset == SuspensionPreset.Custom && SuspensionSettings.SplitFrontRear
              && Near(SuspensionPreset.Custom.SpringFront, 0.5f) && Near(SuspensionPreset.Custom.SpringRear, 2f), "0.5.0 cfg: suspension Custom values kept");
        Check(BrakesSettings.Enabled && BrakesSettings.ActivePreset.Name == "Drift" && Near(BrakesPreset.Custom.ActuationScale, 1.9f)
              && DrivetrainSettings.Enabled && Near(DrivetrainPreset.Custom.PowerScale, 2.5f) && DrivetrainPreset.Custom.DiffFrontMode == DiffMode.LimitedSlip
              && AssistsSettings.Enabled && ModConfig.ToggleKeyString == "F8", "0.5.0 cfg: brakes, drivetrain, assists, toggle key kept");
        string txt = File.ReadAllText(old);
        Check(txt.Contains("RateMultiplier = 1.5") && txt.Contains("SpringFront = 0.5") && txt.Contains("ToggleKey = F8")
              && txt.Contains("[Alignment]") && txt.Contains("[Gearbox.Custom]") && txt.Contains("[Telemetry]") && txt.Contains("FreezeWhileOpen = false"),
            "0.5.0 cfg: no key removed or renamed, new sections appended");
        Check(!UiSettings.FreezeWhileOpen && !UiSettings.TelemetryEnabled, "0.5.0 cfg: new behaviour defaults (live panel, telemetry off)");

        // Hand-edited out-of-range values clamp on load; name-only enum parsing.
        string bad = Path.Combine(dir, "bad060.cfg");
        File.WriteAllText(bad,
            "[Alignment.Custom]\nCamberFL = 99\nPosXRR = -400\n\n[Gearbox.Custom]\nGearCount = 40\nGear3Scale = 0.1\nTransmissionMode = 7\n\n" +
            "[UI]\nPanelWidth = 5000\nPanelScale = 0\nLastTab = 42\n\n[Telemetry]\nPosition = Sideways\n");
        ModConfig.Load(new ConfigFile(bad, true));
        Check(Near(AlignmentPreset.Custom.CamberFL, 45f) && Near(AlignmentPreset.Custom.PosXRR, -100f), "alignment values clamped to +-45 deg / +-100 cm");
        Check(GearboxPreset.Custom.GearCount == 12 && Near(GearboxPreset.Custom.Scale(3), 0.25f), "gear count clamped to 12, gear factor to 0.25");
        Check(GearboxPreset.Custom.TransmissionMode == GearboxMode.Stock && UiSettings.TelemetryPosition == TelemetryCorner.TopLeft,
            "numeric / unknown enum names fall back (Stock, TopLeft)");
        Check(Near(UiSettings.PanelWidth, 1000f) && Near(UiSettings.PanelScale, 0.3f) && UiSettings.LastTab == 8, "panel width/scale/last tab clamped");
        Check(ModConfig.ParseGearboxMode(" manual ") == GearboxMode.Manual && UiSettings.ParseCorner("topright") == TelemetryCorner.TopRight,
            "names parse case/space-tolerant");
        Check(UiSettings.ClampTab(-3) == 0 && UiSettings.ClampTab(10) == 0 && UiSettings.ClampTab(7) == 7, "LastTab parse: garbage -> first tab");

        // Round trip of the new sections.
        ResetAllCategories();
        AlignmentSettings.Enabled = true;
        AlignmentSettings.PerWheel = true;
        AlignmentSettings.SetPresetByName("Sport");
        AlignmentSettings.BeginEdit().PosZRL = -7f;
        GearboxSettings.Enabled = true;
        GearboxPreset gp = GearboxSettings.BeginEdit();
        gp.GearCount = 7;
        gp.SetScale(7, 0.9f);
        gp.TransmissionMode = GearboxMode.Automatic;
        UiSettings.FreezeWhileOpen = true;
        UiSettings.PanelWidth = 620f;
        UiSettings.LastTab = 7;
        UiSettings.TelemetryPosition = TelemetryCorner.TopRight;
        ModConfig.Save();
        ResetAllCategories();
        UiSettings.ResetPanel();
        UiSettings.TelemetryPosition = TelemetryCorner.BottomLeft;
        ModConfig.Load(new ConfigFile(bad, true));
        Check(AlignmentSettings.Enabled && AlignmentSettings.PerWheel && AlignmentSettings.ActivePreset == AlignmentPreset.Custom
              && AlignmentPreset.Custom.BasedOn == "Sport" && Near(AlignmentPreset.Custom.PosZRL, -7f), "alignment Custom persisted");
        Check(GearboxSettings.Enabled && GearboxPreset.Custom.GearCount == 7 && Near(GearboxPreset.Custom.Scale(7), 0.9f)
              && GearboxPreset.Custom.TransmissionMode == GearboxMode.Automatic, "gearbox Custom persisted");
        Check(UiSettings.FreezeWhileOpen && Near(UiSettings.PanelWidth, 620f) && UiSettings.LastTab == 7
              && UiSettings.TelemetryPosition == TelemetryCorner.TopRight, "panel + telemetry settings persisted");
        ResetAllCategories();
        UiSettings.ResetPanel();
        UiSettings.TelemetryPosition = TelemetryCorner.BottomLeft;
    }

    private static bool NoOverlap(params PanelLayout.Band[] b)
    {
        for (int i = 0; i < b.Length; i++)
            for (int j = i + 1; j < b.Length; j++)
                if (b[i].Overlaps(b[j])) return false;
        return true;
    }

    private static void TestPanelLayout()
    {
        Check(Near(PanelLayout.ContentWidth(800f), 748f), "800 px window = 0.5.0's 748 px content width");
        foreach (float W in new[] { 400f, 460f, 800f })
        {
            float c = PanelLayout.ContentWidth(W);
            bool ok = true;
            foreach (bool readout in new[] { false, true })
            {
                PanelLayout.SliderGeom g = PanelLayout.SliderRow(c, readout);
                ok &= g.Title.Inside(c, g.RowHeight) && g.Hint.Inside(c, g.RowHeight) && g.Slider.Inside(c, g.RowHeight)
                      && g.Value.Inside(c, g.RowHeight) && g.Reset.Inside(c, g.RowHeight);
                ok &= NoOverlap(g.Title, g.Slider, g.Value, g.Reset) && NoOverlap(g.Hint, g.Slider, g.Value, g.Reset);
                ok &= g.Slider.W >= 120f && g.Title.W >= 140f;
            }
            PanelLayout.SwitchRowGeom o = PanelLayout.OptionRow(c), m = PanelLayout.MasterRow(c);
            ok &= o.Title.Right <= c - o.SwitchRight - o.SwitchW && o.Hint.Right <= c - o.SwitchRight - o.SwitchW && o.Hint.Bottom <= o.RowHeight;
            ok &= m.Title.Right <= c - m.SwitchRight - m.SwitchW && m.Hint.Right <= c - m.SwitchRight - m.SwitchW && m.Hint.Bottom <= m.RowHeight;
            ok &= o.SwitchH <= o.RowHeight && m.SwitchH <= m.RowHeight;
            float tabW = (W - 2f * PanelLayout.Pad(W) - 4f * 4f) / 5f;
            ok &= tabW >= 60f;
            CurveEditor.Bands cb = CurveEditor.ComputeBands(c, 330);
            ok &= CurveEditor.HintTop + cb.HintHeight <= cb.GraphTop && cb.ReadoutWidth >= CurveEditor.ReadoutMinWidth
                  && c - CurveEditor.Side - CurveEditor.ReadoutRight - cb.ReadoutWidth >= 100f
                  && cb.RowHeight - cb.GraphTop - CurveEditor.GraphBottom >= 150f;
            GearGraph.Bands gb = GearGraph.ComputeBands(c, 110);
            ok &= GearGraph.HintTop + gb.HintHeight <= gb.GraphTop && gb.ReadoutWidth >= GearGraph.ReadoutMinWidth;
            Check(ok, "layout at " + W + " px: rows, switches, tabs, curve editors and gear graph fit without overlap");
        }
        PanelLayout.SliderGeom wide = PanelLayout.SliderRow(748f, true);
        Check(!wide.Stacked && Near(wide.RowHeight, 50f) && Near(wide.Slider.X, 328f) && Near(wide.Reset.X, 748f - 74f)
              && Near(wide.Value.Right, wide.Reset.X - 4f) && Near(wide.Slider.Right, wide.Value.X - 4f),
            "0.7.0 wide rows: 50 px tall, slider from 328, value then Reset at the right (no pin)");
        Check(PanelLayout.SliderRow(PanelLayout.ContentWidth(460f), true).Stacked, "the default 460 px width stacks the rows");

        Check(Near(PanelLayout.EffectiveWidth(800f, 1920f, 1080f, 1f), 800f), "800 px fits a 1080p screen at x1");
        Check(Near(PanelLayout.EffectiveWidth(800f, 1440f, 1080f, 2f), 700f), "x2 on a 4:3 screen: width capped to the canvas (720 - 20)");
        Check(Near(PanelLayout.ScaleFactor(1080f, 1f), 1f) && Near(PanelLayout.ScaleFactor(2160f, 1f), 2f) && Near(PanelLayout.ScaleFactor(1080f, 0.5f), 0.5f),
            "scale factor = Screen.height / 1080 x PanelScale (x1 renders like 0.5.0)");

        Check(PanelLayout.TabForDigit(1) == 0 && PanelLayout.TabForDigit(8) == 7 && PanelLayout.TabForDigit(9) == 8 && PanelLayout.TabForDigit(0) == 8 && PanelLayout.TabForDigit(10) == -1,
            "digit hotkeys: 1..9 -> tabs 1..9, 0 -> tab 9 (Settings)");
        Check(SettingsPanel.TabCount == 9 && SettingsPanel.TabNames.Length == 9 && SettingsPanel.TabNames[7] == "Weight" && SettingsPanel.TabNames[8] == "Settings",
            "nine tabs, Weight second-last, Settings last");

        Check(GearGraph.RouteDrag(true, 3, true) == GearGraph.DragRoute.MoveBar && GearGraph.RouteDrag(true, -1, true) == GearGraph.DragRoute.ScrollList
              && GearGraph.RouteDrag(false, 3, true) == GearGraph.DragRoute.ScrollList && GearGraph.RouteDrag(true, -1, false) == GearGraph.DragRoute.None,
            "gear graph drags: bar = edit, elsewhere / disabled = scroll the list");
        Check(GearGraph.RouteClick(true, false, true, 2) == GearGraph.ClickAction.Select && GearGraph.RouteClick(true, true, true, 2) == GearGraph.ClickAction.None
              && GearGraph.RouteClick(false, false, true, 2) == GearGraph.ClickAction.None && GearGraph.RouteClick(true, false, true, -1) == GearGraph.ClickAction.None,
            "gear graph clicks: select a bar; never after a drag, never while OFF");
        Check(Near(GearGraph.ScaleForDrag(0.5f, 4f, 2f, 0.5f, 1.5f), 1f) && Near(GearGraph.ScaleForDrag(1f, 4f, 2f, 0.5f, 1.5f), 1.5f),
            "bar drag maps height to a clamped factor");
    }

    private static void TestTelemetryAndExtras()
    {
        Check(UiStrings.TelemetrySpeed(72.4f) == "72 km/h" && UiStrings.TelemetryRpm(3456.7f) == "3457 rpm"
              && UiStrings.TelemetryGear("R1") == "Gear R1" && UiStrings.TelemetrySlip(0.1f) == "Slip 9.0°", "telemetry formatting");
        Check(TelemetryStrip.CornerAnchor(TelemetryCorner.TopRight).x == 1f && TelemetryStrip.CornerAnchor(TelemetryCorner.TopRight).y == 1f
              && TelemetryStrip.CornerOffset(TelemetryCorner.BottomLeft, 12f).x == 12f && TelemetryStrip.CornerOffset(TelemetryCorner.TopRight, 12f).y == -12f,
            "telemetry corner anchors point into the screen");

        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        var tuner = new VehicleTuner();
        VehicleTuner.TelemetrySample s;
        NWH.Common.Vehicles.Vehicle.ActiveVehicles.Clear();
        Check(!tuner.TryGetTelemetry(out s), "no active vehicle: no telemetry (strip hidden)");
        VehicleController vc = MakeCar(out FakeWheel[] w, 0f);
        UnityEngine.Object.Registry.Add(vc);
        tuner.ReapplyNow();
        NWH.Common.Vehicles.Vehicle.ActiveVehicles.Add(vc);
        vc.powertrain.engine.OutputRPM = 2500f;
        w[0].SetLateralSlip(0.1f);
        w[1].SetLateralSlip(-0.3f);
        w[2].SetLateralSlip(5f);   // rear: not part of the front reading
        Check(tuner.TryGetTelemetry(out s) && Near(s.SpeedKmh, 36f) && Near(s.Rpm, 2500f) && s.Gear == "N" && Near(s.FrontSlip, 0.2f),
            "telemetry: speed x3.6, engine RPM, GearName, mean |front lateral slip|");
        NWH.Common.Vehicles.Vehicle.ActiveVehicles.Clear();
        Check(Near(tuner.MeanBaseline(VehicleTuner.Readout.BrakeTorque, true), 7000f), "brake-torque readout baseline = stock maxTorque");

        float armed = -1f;
        Check(!SettingsPanel.ConfirmTwoClick(ref armed, 10f) && armed > 10f, "'Turn everything off': first click only arms");
        Check(SettingsPanel.ConfirmTwoClick(ref armed, 11f), "second click within 3 s confirms");
        armed = -1f;
        SettingsPanel.ConfirmTwoClick(ref armed, 20f);
        Check(!SettingsPanel.ConfirmTwoClick(ref armed, 24f), "an expired arm re-arms instead of confirming");
        SteeringSettings.Enabled = SuspensionSettings.Enabled = AeroSettings.Enabled = BrakesSettings.Enabled = GripSettings.Enabled = true;
        DrivetrainSettings.Enabled = AssistsSettings.Enabled = AlignmentSettings.Enabled = GearboxSettings.Enabled = true;
        SettingsPanel.TurnEverythingOff();
        Check(!SteeringSettings.Enabled && !SuspensionSettings.Enabled && !AeroSettings.Enabled && !BrakesSettings.Enabled && !GripSettings.Enabled
              && !DrivetrainSettings.Enabled && !AssistsSettings.Enabled && !AlignmentSettings.Enabled && !GearboxSettings.Enabled,
            "'Turn everything off' switches all nine categories off");
        ResetAllCategories();
    }

    // ================================================================ 0.6.2 drivetrain layout

    private sealed class LayoutRig
    {
        public VehicleController Vc;
        public FakeWheel[] W;              // FL, FR, A2L, A2R, ... (front to back, left first)
        public WheelComponent[] Wc;
        public DifferentialComponent[] AxleDiffs;
        public DifferentialComponent Centre;
    }

    /// <summary>
    /// A car wired the way NWH's auto-setup wires one (Powertrain.cs:118-165): one Open diff per
    /// axle (Output = left, OutputB = right), and either a centre diff over the first two axle
    /// diffs (awd) or the gearbox straight into the last axle's diff (rear-drive).
    /// </summary>
    private static LayoutRig MakeLayoutCar(int axles, bool awd, string name)
    {
        var rig = new LayoutRig();
        var vc = new VehicleController();
        vc.gameObject.name = name;
        vc.vehicleRigidbody = new Rigidbody();
        vc.fixedDeltaTime = 0.02f;
        vc.moduleManager.vehicleController = vc;
        vc.powertrain.transmission.name = "Transmission";
        rig.Vc = vc;
        rig.W = new FakeWheel[axles * 2];
        rig.Wc = new WheelComponent[axles * 2];
        rig.AxleDiffs = new DifferentialComponent[axles];
        for (int a = 0; a < axles; a++)
        {
            var group = new WheelGroup();
            for (int side = 0; side < 2; side++)
            {
                var w = new FakeWheel { SpringMaxForce = 30000f, SpringMaxLength = 0.3f };
                w.transform.position = new Vector3(side == 0 ? -0.8f : 0.8f, 0f, 1.5f - a * 1.5f);
                var wc = new WheelComponent { name = "Wheel" + (a * 2 + side), wheelUAPI = w, vehicleController = vc };
                rig.W[a * 2 + side] = w;
                rig.Wc[a * 2 + side] = wc;
                group.Wheels.Add(wc);
                vc.powertrain.wheels.Add(wc);
            }
            vc.powertrain.wheelGroups.Add(group);
            var diff = new DifferentialComponent { name = "Axle Diff " + (a + 1), DifferentialType = DifferentialComponent.Type.Open };
            diff.Output = rig.Wc[a * 2];
            diff.OutputB = rig.Wc[a * 2 + 1];
            rig.AxleDiffs[a] = diff;
            vc.powertrain.differentials.Add(diff);
        }
        if (awd)
        {
            rig.Centre = new DifferentialComponent { name = "Center Differential", DifferentialType = DifferentialComponent.Type.LimitedSlip, biasAB = 0.4f };
            rig.Centre.Output = rig.AxleDiffs[0];
            rig.Centre.OutputB = rig.AxleDiffs[1];
            vc.powertrain.differentials.Add(rig.Centre);
            vc.powertrain.transmission.Output = rig.Centre;
        }
        else
        {
            vc.powertrain.transmission.Output = rig.AxleDiffs[axles - 1];
        }
        return rig;
    }

    /// <summary>One powertrain step below the gearbox, as TransmissionComponent.ForwardStep does it (:420-433, ratio 1).</summary>
    private static void StepDrive(LayoutRig rig, float torque)
    {
        for (int i = 0; i < rig.W.Length; i++)
        {
            rig.W[i].StepCount = 0;
        }
        TransmissionComponent t = rig.Vc.powertrain.transmission;
        if (t.outputNameHash != 0 && t.Output != null)
        {
            t.Output.ForwardStep(torque, 0f, 0.02f);
        }
    }

    private static string StepCounts(LayoutRig rig)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < rig.W.Length; i++) sb.Append(rig.W[i].StepCount);
        return sb.ToString();
    }

    private static void TestLayoutParser()
    {
        DrivetrainLayout l;
        string err;
        Check(DrivetrainLayout.TryParse(DrivetrainSettings.DefaultLayoutText, out l, out err) && l.Nodes.Length == 3
              && l.Root.Kind == DrivetrainLayout.TargetKind.Node && l.Nodes[l.Root.Node].Name == "transfer"
              && Near(l.Nodes[l.Root.Node].Split, 0.4f) && l.Nodes[2].Type == DiffMode.LimitedSlip,
            "default layout parses: transfer (Open, split 0.4) -> front, rear (LSD)");
        Check(DrivetrainLayout.TryParse("  GEARBOX->r ;; r : lsd STIFFNESS=0.7 slip=900 power=0.8 coast=0.2 -> rl , rr ; ", out l, out err)
              && Near(l.Nodes[0].Stiffness, 0.7f) && Near(l.Nodes[0].SlipTorque, 900f) && Near(l.Nodes[0].PowerRamp, 0.8f)
              && Near(l.Nodes[0].CoastRamp, 0.2f) && l.Nodes[0].A.Axle == 0 && l.Nodes[0].A.Side == 'L',
            "case/space tolerant, empty statements ignored, all LSD keys read (" + err + ")");
        Check(DrivetrainLayout.TryParse("transmission -> A2", out l, out err) && l.Nodes.Length == 0
              && l.Root.Kind == DrivetrainLayout.TargetKind.Wheel && l.Root.Axle == 2 && l.Root.Side == 'C',
            "the gearbox can drive one wheel directly ('transmission' accepted)");

        DrivetrainLayout.Target t;
        Check(DrivetrainLayout.TryParseWheel("a12r", out t) && t.Axle == 12 && t.Side == 'R', "wheel token A12R");
        Check(!DrivetrainLayout.TryParseWheel("A0L", out t) && !DrivetrainLayout.TryParseWheel("AxL", out t)
              && !DrivetrainLayout.TryParseWheel("A1LR", out t) && !DrivetrainLayout.TryParseWheel("A", out t),
            "bad wheel tokens rejected (A0L, AxL, A1LR, A)");

        string[,] bad =
        {
            { "", "empty" },
            { "r: Open -> RL, RR", "gearbox" },
            { "gearbox -> RL; gearbox -> RR", "twice" },
            { "gearbox -> RL, RR", "exactly one" },
            { "gearbox -> r; r: Stock -> RL, RR", "unknown type" },
            { "gearbox -> r; r: Viscous -> RL, RR", "unknown type" },
            { "gearbox -> r; r: Open bias=0.3 -> RL, RR", "unknown key" },
            { "gearbox -> r; r: Open split=1.5 -> RL, RR", "outside" },
            { "gearbox -> r; r: Open split=0,4 -> RL, RR", "not a number" },
            { "gearbox -> r; r: Open -> RL", "two outputs" },
            { "gearbox -> r; r: Open -> RL, RR, FL", "two outputs" },
            { "gearbox -> r; r: Open -> RL, RR; r: Open -> FL, FR", "defined twice" },
            { "gearbox -> r; r: Open -> RL, nowhere", "neither" },
            { "gearbox -> r; r: Open -> RL, gearbox", "gearbox cannot be an output" },
            { "gearbox -> r; r: Open -> r, RR", "feeds itself" },
            { "gearbox -> a; a: Open -> b, FL; b: Open -> FR, RL; c: Open -> b, RR", "fed twice" },
            { "gearbox -> RL; a: Open -> b, FL; b: Open -> a, FR", "not connected" },   // a detached cycle
            { "gearbox -> RL; a: Open -> FL, FR", "not connected" },
            { "gearbox -> r; r: Open -> FL, FL", "driven twice" },
            { "gearbox -> FL; FL: Open -> RL, RR", "wheel name" },
            { "gearbox -> 4wd; 4wd: Open -> RL, RR", "start with a letter" },
            { "gearbox -> r; r: Open -> RL, RR -> FL", "one '->'" },
            { "gearbox RL", "missing '->'" },
        };
        for (int i = 0; i < bad.GetLength(0); i++)
        {
            bool ok = DrivetrainLayout.TryParse(bad[i, 0], out l, out err);
            Check(!ok && l == null && err != null && err.IndexOf(bad[i, 1], StringComparison.OrdinalIgnoreCase) >= 0,
                "rejected: '" + bad[i, 0] + "' -> " + err);
        }
    }

    private static void TestLayoutRuntime()
    {
        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        TargetSettings.Mode = TargetMode.All;
        TargetSettings.SelectedName = "";

        // ---- a 4-wheel AWD car the game has already been driving (stepped once before capture)
        LayoutRig awd = MakeLayoutCar(2, true, "Duke(Clone)");
        StepDrive(awd, 100f);
        Check(StepCounts(awd) == "1111" && !awd.W[0].AutoSimulate && awd.W[0].Inertia > 1.2f,
            "rig: stock AWD steps every wheel once and the powertrain owns them (AutoSimulate off, reflected inertia)");
        UnityEngine.Object.Registry.Add(awd.Vc);
        var tuner = new VehicleTuner();
        tuner.ReapplyNow();

        TransmissionComponent t = awd.Vc.powertrain.transmission;
        PowertrainComponent stockOut = t.Output;
        int stockOutHash = t.outputNameHash;
        var stockIn = new PowertrainComponent[4];
        var stockInHash = new int[4];
        for (int i = 0; i < 4; i++) { stockIn[i] = awd.Wc[i].Input; stockInHash[i] = awd.Wc[i].inputNameHash; }

        string stock = tuner.StockLayoutText(awd.Vc);
        DrivetrainLayout parsed;
        string err;
        Check(stock != null && DrivetrainLayout.TryParse(stock, out parsed, out err) && parsed.Nodes.Length == 3,
            "the vehicle's own wiring is logged as layout text that parses back: " + stock);
        Check(stock != null && stock.IndexOf("LSD", StringComparison.Ordinal) >= 0 && stock.IndexOf("split=0.6", StringComparison.Ordinal) >= 0
              && stock.IndexOf("-> FL, FR", StringComparison.Ordinal) >= 0 && stock.IndexOf("-> RL, RR", StringComparison.Ordinal) >= 0,
            "stock text keeps the centre diff's type and split (biasAB 0.4 = 60% to output A) and the wheel tokens");

        // Layout on: rear-drive with an LSD.
        DrivetrainSettings.Enabled = true;
        DrivetrainSettings.LayoutEnabled = true;
        DrivetrainSettings.LayoutText = "gearbox -> rear; rear: LSD -> RL, RR";
        tuner.ApplyLive();
        var node = t.Output as DifferentialComponent;
        Check(node != null && node != stockOut && node.name == "AVT rear", "the gearbox now drives the layout's root node");
        Check(awd.Vc.powertrain.differentials.Count == 3, "layout nodes are NOT added to powertrain.differentials");
        Check(t.outputNameHash == stockOutHash, "the transmission keeps its stock output hash (a save can't bake a dangling name)");
        bool hashesStock = true;
        for (int i = 0; i < 4; i++) hashesStock &= awd.Wc[i].inputNameHash == stockInHash[i];
        Check(hashesStock, "every wheel keeps its stock input hash");
        StepDrive(awd, 100f);
        Check(StepCounts(awd) == "0011", "only RL/RR are stepped by the powertrain, once each (" + StepCounts(awd) + ")");
        Check(Near(awd.W[2].MotorTorque + awd.W[3].MotorTorque, 100f), "all torque reaches the rear wheels");
        Check(awd.W[0].AutoSimulate && awd.W[1].AutoSimulate && Near(awd.W[0].MotorTorque, 0f) && Near(awd.W[0].Inertia, 1.2f),
            "released front wheels simulate themselves again, with no motor torque and their own inertia");
        Check(awd.Centre.Output == awd.AxleDiffs[0] && awd.Centre.OutputB == awd.AxleDiffs[1]
              && awd.Centre.DifferentialType == DifferentialComponent.Type.LimitedSlip && Near(awd.Centre.biasAB, 0.4f)
              && awd.AxleDiffs[0].Output == awd.Wc[0],
            "the vehicle's own diffs are bypassed, never edited");

        int assigns = node.TypeAssignments;
        tuner.ApplyLive();
        tuner.ApplyLive();
        StepDrive(awd, 100f);
        Check(t.Output == node && node.TypeAssignments == assigns && StepCounts(awd) == "0011",
            "re-applying is idempotent (same node, no split-delegate churn, still one step per wheel)");

        // A new Open node: Open is also the field default, so an assign-on-change rule would
        // leave NWH's split delegate null and ForwardStep would throw every tick.
        DrivetrainSettings.LayoutText = "gearbox -> rear; rear: Open -> RL, RR";
        tuner.ApplyLive();
        bool threw = false;
        try { StepDrive(awd, 50f); } catch (NullReferenceException) { threw = true; }
        Check(!threw && Near(awd.W[2].MotorTorque, 25f), "a new Open node has its split delegate assigned");

        // Full-time AWD with a 40/60 open transfer case (the shipped example).
        DrivetrainSettings.LayoutText = DrivetrainSettings.DefaultLayoutText;
        tuner.ApplyLive();
        StepDrive(awd, 100f);
        Check(StepCounts(awd) == "1111" && Near(awd.W[0].MotorTorque + awd.W[1].MotorTorque, 40f)
              && Near(awd.W[2].MotorTorque + awd.W[3].MotorTorque, 60f) && !awd.W[0].AutoSimulate,
            "Open transfer case split=0.4: 40% front / 60% rear, every wheel stepped once");

        // Layout OFF (category stays on): exact restore of references and hashes.
        DrivetrainSettings.LayoutEnabled = false;
        tuner.ApplyLive();
        bool inputsStock = true;
        for (int i = 0; i < 4; i++) inputsStock &= awd.Wc[i].Input == stockIn[i] && awd.Wc[i].inputNameHash == stockInHash[i];
        Check(t.Output == stockOut && t.outputNameHash == stockOutHash && stockOut.Input == t && inputsStock,
            "layout OFF restores the gearbox output, the root's input and every wheel's input + hash");
        StepDrive(awd, 100f);
        Check(StepCounts(awd) == "1111", "the stock AWD drivetrain steps all four wheels again");

        // A layout that does not fit this vehicle: it keeps its own drivetrain.
        DrivetrainSettings.LayoutEnabled = true;
        DrivetrainSettings.LayoutText = "gearbox -> rear; rear: Open -> A3L, A3R";
        tuner.ApplyLive();
        string problem = tuner.LayoutProblem(awd.Vc);
        Check(t.Output == stockOut && problem != null && problem.IndexOf("2 axles", StringComparison.Ordinal) >= 0,
            "an axle the vehicle lacks: layout skipped, own drivetrain kept (" + problem + ")");

        // Applied, then a layout that only collides on this vehicle (RL = A2L on two axles): restored.
        DrivetrainSettings.LayoutText = "gearbox -> rear; rear: LSD -> RL, RR";
        tuner.ApplyLive();
        Check(t.Output != stockOut, "(applied again)");
        DrivetrainSettings.LayoutText = "gearbox -> rear; rear: Open -> RL, A2L";
        tuner.ApplyLive();
        problem = tuner.LayoutProblem(awd.Vc);
        Check(t.Output == stockOut && problem != null && problem.IndexOf("driven twice", StringComparison.Ordinal) >= 0,
            "RL and A2L name the same wheel here: rejected and the previous layout restored (" + problem + ")");

        // Invalid text while enabled: nothing changes, the reason is available.
        DrivetrainSettings.LayoutText = "gearbox -> nowhere";
        tuner.ApplyLive();
        Check(t.Output == stockOut && DrivetrainSettings.Layout == null && DrivetrainSettings.LayoutError != null
              && tuner.LayoutProblem(awd.Vc) == DrivetrainSettings.LayoutError,
            "invalid layout text: every vehicle keeps its own drivetrain (" + DrivetrainSettings.LayoutError + ")");

        // The gearbox straight into one wheel.
        DrivetrainSettings.LayoutText = "gearbox -> RL";
        tuner.ApplyLive();
        StepDrive(awd, 100f);
        Check(t.Output == awd.Wc[2] && StepCounts(awd) == "0010" && Near(awd.W[2].MotorTorque, 100f) && awd.W[3].AutoSimulate,
            "'gearbox -> RL' drives that one wheel and releases the rest");

        // Category OFF restores too.
        DrivetrainSettings.LayoutText = "gearbox -> rear; rear: LSD -> RL, RR";
        tuner.ApplyLive();
        DrivetrainSettings.Enabled = false;
        tuner.ApplyLive();
        StepDrive(awd, 100f);
        Check(t.Output == stockOut && StepCounts(awd) == "1111", "Drivetrain OFF restores the vehicle's own wiring");

        // Runner disabled (OnDisable restores, like every category).
        DrivetrainSettings.Enabled = true;
        tuner.ApplyLive();
        Check(t.Output != stockOut, "(applied again)");
        Invoke(tuner, "OnDisable");
        Check(t.Output == stockOut && t.outputNameHash == stockOutHash, "runner OnDisable restores the layout");
        Invoke(tuner, "OnEnable");

        // ---- multi-axle layout (legacy configs): the parser still accepts 6x6-style text
        // even though the panel no longer offers a 6x6 template (0.7.1; the game has none).
        UnityEngine.Object.Registry.Clear();
        LayoutRig six = MakeLayoutCar(3, false, "Rustliner(Clone)");
        StepDrive(six, 100f);
        UnityEngine.Object.Registry.Add(six.Vc);
        var t6 = new VehicleTuner();
        t6.ReapplyNow();
        string sixStock = t6.StockLayoutText(six.Vc);
        Check(sixStock != null && sixStock.IndexOf("-> RL, RR", StringComparison.Ordinal) >= 0 && sixStock.IndexOf("A2", StringComparison.Ordinal) < 0,
            "6x6 stock text: only the last axle is driven (" + sixStock + ")");
        DrivetrainSettings.Enabled = true;
        DrivetrainSettings.LayoutEnabled = true;
        DrivetrainSettings.LayoutText = "gearbox -> transfer; transfer: Locked -> front, bogie; front: Open -> FL, FR; "
            + "bogie: Locked -> mid, rear; mid: Open -> A2L, A2R; rear: Open -> RL, RR";
        t6.ApplyLive();
        StepDrive(six, 80f);
        Check(StepCounts(six) == "111111", "6x6 layout: all six wheels stepped exactly once (" + StepCounts(six) + ")");
        Check(Near(six.W[0].MotorTorque, 20f) && Near(six.W[2].MotorTorque, 10f) && Near(six.W[5].MotorTorque, 10f),
            "6x6 torque: 1/2 to the front axle, 1/4 each to the middle and rear (locked, equal wheel speeds)");
        Check(!six.W[0].AutoSimulate && !six.W[2].AutoSimulate, "previously undriven wheels are now owned by the powertrain");
        DrivetrainSettings.LayoutEnabled = false;
        t6.ApplyLive();
        StepDrive(six, 80f);
        Check(StepCounts(six) == "000011" && six.W[0].AutoSimulate && six.W[2].AutoSimulate && Near(six.W[0].MotorTorque, 0f)
              && Near(six.W[0].Inertia, 1.2f),
            "OFF: back to rear-drive; the front/middle wheels simulate themselves again with their own inertia");

        // ---- targeting: Selected vehicle only, switching restores the old one.
        UnityEngine.Object.Registry.Clear();
        LayoutRig a = MakeLayoutCar(2, true, "Outrider(Clone)");
        LayoutRig b = MakeLayoutCar(2, true, "Junker(Clone)");
        UnityEngine.Object.Registry.Add(a.Vc);
        UnityEngine.Object.Registry.Add(b.Vc);
        var t2 = new VehicleTuner();
        t2.ReapplyNow();
        DrivetrainSettings.LayoutEnabled = true;
        DrivetrainSettings.LayoutText = "gearbox -> rear; rear: LSD -> RL, RR";
        TargetSettings.Mode = TargetMode.Selected;
        TargetSettings.SelectedName = "Outrider(Clone)";
        t2.ApplyLive();
        Check(a.Vc.powertrain.transmission.Output != a.Centre && b.Vc.powertrain.transmission.Output == b.Centre,
            "Selected: only the selected vehicle gets the layout");
        TargetSettings.SelectedName = "Junker(Clone)";
        t2.ApplyLive();
        Check(a.Vc.powertrain.transmission.Output == a.Centre && b.Vc.powertrain.transmission.Output != b.Centre,
            "switching the selection restores the old vehicle and rewires the new one");
        TargetSettings.Mode = TargetMode.All;
        TargetSettings.SelectedName = "";
        DrivetrainSettings.Enabled = false;
        t2.ApplyLive();
        Check(a.Vc.powertrain.transmission.Output == a.Centre && b.Vc.powertrain.transmission.Output == b.Centre, "OFF restores both");
        ResetAllCategories();
        UnityEngine.Object.Registry.Clear();
    }

    private static void TestLayoutConfig(string dir)
    {
        string path = Path.Combine(dir, "layout.cfg");
        ModConfig.Load(new ConfigFile(path, true));
        Check(!DrivetrainSettings.LayoutEnabled && DrivetrainSettings.LayoutText == DrivetrainSettings.DefaultLayoutText
              && DrivetrainSettings.Layout != null,
            "fresh config: layout off, the example text present and valid");
        string text = "gearbox ->  rear ; rear: LSD  stiffness=0.65 -> RL, RR";
        DrivetrainSettings.LayoutEnabled = true;
        DrivetrainSettings.LayoutText = text;
        ModConfig.Save();
        DrivetrainSettings.LayoutEnabled = false;
        DrivetrainSettings.LayoutText = "gearbox -> FL";
        ModConfig.Load(new ConfigFile(path, true));
        Check(DrivetrainSettings.LayoutEnabled && DrivetrainSettings.LayoutText == text && DrivetrainSettings.Layout != null
              && Near(DrivetrainSettings.Layout.Nodes[0].Stiffness, 0.65f),
            "layout Enabled + text round-trip through the file verbatim");
        Check(File.ReadAllText(path).IndexOf("[Drivetrain.Layout]", StringComparison.Ordinal) >= 0, "written under [Drivetrain.Layout]");

        string broken = Path.Combine(dir, "layout-broken.cfg");
        File.WriteAllText(broken, "[Drivetrain.Layout]\nEnabled = true\nLayout = gearbox -> a; a: Open -> b, RL; b: Open -> a, RR\n");
        ModConfig.Load(new ConfigFile(broken, true));
        Check(DrivetrainSettings.LayoutEnabled && DrivetrainSettings.Layout == null && DrivetrainSettings.LayoutError != null,
            "a hand-edited cyclic layout loads without throwing and is rejected (" + DrivetrainSettings.LayoutError + ")");

        DrivetrainSettings.LayoutEnabled = true;
        DrivetrainSettings.ResetAll();
        Check(!DrivetrainSettings.LayoutEnabled && DrivetrainSettings.LayoutText.IndexOf("a: Open", StringComparison.Ordinal) >= 0,
            "Reset-all turns the layout off but keeps the user's text");
        ModConfig.Load(new ConfigFile(Path.Combine(dir, "layout-reset.cfg"), true));
        ResetAllCategories();
    }

    private static void TestCurveEditorPick()
    {
        // 300x190 graph, centred pivot: the middle handle (0.5, 0.5) sits at local (0, 0).
        EditableCurve curve = EditableCurve.FromPoints(0f, 0f, 0.5f, 0.5f, 1f, 1f);
        Check(CurveEditor.PickHandleAt(curve, new Vector2(2f, 1f), 300f, 190f, 0.5f, 0.5f) == 1, "press on a handle picks it");
        Check(CurveEditor.PickHandleAt(curve, new Vector2(18f, 1f), 300f, 190f, 0.5f, 0.5f) == -1,
            "16 px further on — where OnBeginDrag fires on a quick pull past the 10 px drag threshold — the same handle "
            + "is outside the 14 px pick radius: the drag must pick at pressPosition (0.6.0 used the current position and scrolled instead)");
        Check(CurveEditor.PickHandleAt(null, Vector2.zero, 300f, 190f, 0.5f, 0.5f) == -1, "no curve: no handle");
    }

    private static void TestApplyAllocation()
    {
        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        TargetSettings.Mode = TargetMode.All;
        LayoutRig rig = MakeLayoutCar(2, true, "Duke(Clone)");
        UnityEngine.Object.Registry.Add(rig.Vc);
        var tuner = new VehicleTuner();
        tuner.ReapplyNow();
        SuspensionSettings.Enabled = true; SuspensionSettings.SetPresetByName("Race");
        BrakesSettings.Enabled = true; BrakesSettings.SetPresetByName("Race");
        GripSettings.Enabled = true; GripSettings.SetPresetByName("Race");
        DrivetrainSettings.Enabled = true; DrivetrainSettings.SetPresetByName("Race");
        AssistsSettings.Enabled = true; AssistsSettings.SetPresetByName("Standard");
        AlignmentSettings.Enabled = true; AlignmentSettings.SetPresetByName("Race");
        GearboxSettings.Enabled = true; GearboxSettings.SetPresetByName("Truck");   // 0.7.0: spread + shift controller hook
        DrivetrainSettings.LayoutEnabled = true;
        DrivetrainSettings.LayoutText = DrivetrainSettings.DefaultLayoutText;
        tuner.ApplyLive();   // first apply: baseline refresh + layout resolve (allocates by design)
        tuner.ApplyLive();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            tuner.ApplyLive();
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, "steady-state ApplyLive with seven categories (0.7.0: + Gearbox Truck) + a layout allocates nothing (" + bytes + " bytes over 50 passes; "
            + "0.6.0 built a closure per category per pass)");
        ResetAllCategories();
        tuner.ApplyLive();
        UnityEngine.Object.Registry.Clear();
    }

    // ================================================================ 0.6.3

    private static void TestDrivenPick()
    {
        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        TargetSettings.Mode = TargetMode.All;
        TargetSettings.SelectedName = "";
        // Three idling cars; the player drives the SECOND one tracked.
        VehicleController a = MakeCar(out FakeWheel[] wa, 0f);
        VehicleController b = MakeCar(out FakeWheel[] wb, 0f);
        VehicleController c = MakeCar(out FakeWheel[] wc, 0f);
        a.gameObject.name = "Parked A";
        b.gameObject.name = "Player B";
        c.gameObject.name = "Parked C";
        foreach (VehicleController v in new[] { a, b, c })
        {
            v.powertrain.engine.OutputRPM = 900f;   // all engines idling
            UnityEngine.Object.Registry.Add(v);
        }
        var tuner = new VehicleTuner();
        tuner.ReapplyNow();

        b.input.Throttle = 1f;
        Check(tuner.FindDrivenVehicle() == b, "live input picks the player's car");
        b.input.Throttle = 0f;   // hands off the keys (clicking a panel setting)
        Check(tuner.FindDrivenVehicle() == b,
            "hands off: the last-driven car keeps the pick (0.6.2: an idling engine counted as input, the first tracked idler won)");
        VehicleTuner.TelemetrySample sample;
        b.Speed = 12f;
        Check(tuner.TryGetTelemetry(out sample) && Near(sample.SpeedKmh, 43.2f),
            "telemetry shows the player's car with hands off, not a parked car's zeros");
        b.Speed = 0f;

        // "Apply to: Last driven" must not hop to a parked car when the player clicks a setting.
        TargetSettings.Mode = TargetMode.LastDriven;
        SuspensionSettings.Enabled = true;
        SuspensionSettings.SetPresetByName("Race");
        b.input.Steering = 0.5f;
        tuner.ApplyLive();
        b.input.Steering = 0f;
        SuspensionSettings.BeginEdit().SpringFront = 1.7f;   // a panel edit with hands off the keys
        tuner.ApplyLive();
        Check(Near(wb[0].SpringMaxForce, 30000f * 1.7f) && Near(wa[0].SpringMaxForce, 30000f) && Near(wc[0].SpringMaxForce, 30000f),
            "Last driven + hands off: the edit lands on the player's car, the parked cars stay stock");

        // No input ever (fresh load): a running engine still beats dead ones, below the memory.
        UnityEngine.Object.Registry.Clear();
        VehicleController dead = MakeCar(out FakeWheel[] _, 0f);
        VehicleController idle = MakeCar(out FakeWheel[] _, 0f);
        dead.powertrain.engine.OutputRPM = 0f;
        idle.powertrain.engine.OutputRPM = 800f;
        UnityEngine.Object.Registry.Add(dead);
        UnityEngine.Object.Registry.Add(idle);
        var t2 = new VehicleTuner();
        t2.ReapplyNow();
        Check(t2.FindDrivenVehicle() == idle, "no input yet: a running engine beats a dead one");
        SuspensionSettings.Enabled = false;
        tuner.ApplyLive();
        ResetAllCategories();
        TargetSettings.Mode = TargetMode.All;
        UnityEngine.Object.Registry.Clear();
    }

    private static void TestFaultGuards()
    {
        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        TargetSettings.Mode = TargetMode.All;

        // (a) Capture: a vehicle whose module manager throws (half-initialised mid-spawn).
        VehicleController ok = MakeCar(out FakeWheel[] wOk, 0f);
        VehicleController half = MakeCar(out FakeWheel[] wHalf, 0f);
        var mm = new ThrowingModuleManager { vehicleController = half };
        half.moduleManager = mm;
        UnityEngine.Object.Registry.Add(ok);
        UnityEngine.Object.Registry.Add(half);
        var tuner = new VehicleTuner();
        bool threw = false;
        try { tuner.ReapplyNow(); } catch (Exception) { threw = true; }
        Check(!threw && tuner.TrackedVehicles == 1, "a capture that throws in one category does not escape; the healthy vehicle is tracked");
        mm.Throw = false;    // the vehicle finished initialising
        tuner.ReapplyNow();
        Check(tuner.TrackedVehicles == 2, "the half-initialised vehicle is captured on a later scan once it stops throwing");

        UnityEngine.Object.Registry.Clear();
        VehicleController broken = MakeCar(out FakeWheel[] wBroken, 0f);
        broken.moduleManager = new ThrowingModuleManager { vehicleController = broken };
        UnityEngine.Object.Registry.Add(broken);
        var t2 = new VehicleTuner();
        for (int i = 0; i < VehicleTuner.MaxCaptureAttempts; i++) t2.ReapplyNow();
        Check(t2.TrackedVehicles == 1, "a vehicle that keeps throwing is kept after " + VehicleTuner.MaxCaptureAttempts
            + " attempts with the categories that did capture");
        SuspensionSettings.Enabled = true;
        SuspensionSettings.SetPresetByName("Race");
        AeroSettings.Enabled = true;
        AeroSettings.SetPresetByName("Race");
        threw = false;
        try { t2.ApplyLive(); } catch (Exception) { threw = true; }
        Check(!threw && !Near(wBroken[0].SpringMaxForce, 30000f), "its failed category (aero) is skipped, suspension still applies");
        ResetAllCategories();
        t2.ApplyLive();
        Check(Near(wBroken[0].SpringMaxForce, 30000f), "and restores");

        // (b) Apply: one vehicle's wheel throws mid-pass. 0.6.2 let it escape ApplyLive, which
        // aborted that category for every later vehicle and every later category.
        UnityEngine.Object.Registry.Clear();
        VehicleController bad = MakeCar(out FakeWheel[] _, 0f);
        var tw = new ThrowingWheel();
        tw.transform.position = new Vector3(0f, 0f, 1.3f);
        var twc = new WheelComponent { wheelUAPI = tw };
        bad.powertrain.wheelGroups[0].Wheels.Add(twc);
        bad.powertrain.wheels.Add(twc);
        VehicleController good = MakeCar(out FakeWheel[] wGood, 0f);
        UnityEngine.Object.Registry.Add(bad);
        UnityEngine.Object.Registry.Add(good);
        var t3 = new VehicleTuner();
        t3.ReapplyNow();
        tw.Throw = true;
        SuspensionSettings.Enabled = true;
        SuspensionSettings.SetPresetByName("Race");
        BrakesSettings.Enabled = true;
        BrakesSettings.SetPresetByName("Race");
        threw = false;
        try { t3.ApplyLive(); } catch (Exception) { threw = true; }
        Check(!threw, "an apply that throws on one vehicle does not escape ApplyLive");
        Check(!Near(wGood[0].SpringMaxForce, 30000f), "the other vehicle still gets the category");
        Check(!Near(good.brakes.maxTorque, 7000f) && !Near(bad.brakes.maxTorque, 7000f),
            "later categories still apply to every vehicle, the faulty one included");
        tw.Throw = false;
        ResetAllCategories();
        t3.ApplyLive();
        Check(Near(wGood[0].SpringMaxForce, 30000f) && Near(good.brakes.maxTorque, 7000f) && Near(bad.brakes.maxTorque, 7000f),
            "OFF restores everything, including the categories applied around the fault");
        UnityEngine.Object.Registry.Clear();
    }

    private static void TestSpawnGate()
    {
        Check(VehicleTuner.ShouldSkipScan(1f, 5f) && !VehicleTuner.ShouldSkipScan(5f, 5f), "quiet window: skip before its end, scan after");
        Check(!VehicleTuner.ShouldDeferCapture(30, -1, 0), "first scan of a runner never defers (no previous count)");
        Check(VehicleTuner.ShouldDeferCapture(6, 3, 0) && !VehicleTuner.ShouldDeferCapture(5, 3, 0),
            "a jump of more than " + VehicleTuner.SpawnJump + " vehicles defers capture");
        Check(!VehicleTuner.ShouldDeferCapture(20, 3, VehicleTuner.MaxSpawnDeferrals), "never more than " + VehicleTuner.MaxSpawnDeferrals + " deferrals in a row");

        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        Time.unscaledTime = 100f;
        VehicleController first = MakeCar(out FakeWheel[] _, 0f);
        UnityEngine.Object.Registry.Add(first);
        var tuner = new VehicleTuner();
        tuner.ReapplyNow();
        Check(tuner.TrackedVehicles == 1, "(baseline scan before the load)");

        VehicleTuner.NotifySceneLoaded();     // the game loads a save
        for (int i = 0; i < 4; i++)
        {
            VehicleController v = MakeCar(out FakeWheel[] _, 0f);
            UnityEngine.Object.Registry.Add(v);
        }
        Time.unscaledTime = 102f;
        tuner.ReapplyNow();
        Check(tuner.TrackedVehicles == 1, "inside the " + VehicleTuner.SpawnQuietSeconds + " s post-load window nothing new is captured");
        Time.unscaledTime = 106f;
        tuner.ReapplyNow();
        Check(tuner.TrackedVehicles == 1, "after the window, a jump of 4 vehicles defers capture one scan (spawn wave still running)");
        tuner.ReapplyNow();
        Check(tuner.TrackedVehicles == 5, "the next scan with a stable count captures them all");

        // Endless growth cannot starve capture.
        for (int round = 0; round < VehicleTuner.MaxSpawnDeferrals + 1; round++)
        {
            for (int i = 0; i < 3; i++)
            {
                VehicleController v = MakeCar(out FakeWheel[] _, 0f);
                UnityEngine.Object.Registry.Add(v);
            }
            tuner.ReapplyNow();
        }
        Check(tuner.TrackedVehicles == 5 + 3 * (VehicleTuner.MaxSpawnDeferrals + 1),
            "steady growth defers at most " + VehicleTuner.MaxSpawnDeferrals + " scans, then captures");

        // Reset the static window so nothing after this test is gated.
        Time.unscaledTime = -VehicleTuner.SpawnQuietSeconds;
        VehicleTuner.NotifySceneLoaded();
        Time.unscaledTime = 0f;
        UnityEngine.Object.Registry.Clear();
    }

    // ================================================================ 0.6.4

    /// <summary>
    /// The game's FSM writes vc.input.* every frame only while a vehicle is driven; on exit
    /// the values freeze (handbrake held, brakes last pressed, …). 0.6.3 scored raw input,
    /// so a parked car with frozen input out-scored a hands-off player and stole the pick —
    /// the strip showed that car's zeros until the player steered. 0.6.4: only input that
    /// changed recently (2 s hold) is live.
    /// </summary>
    private static void TestSaveAndSteerExtras(string dir)
    {
        // MaxSteerAngle config round-trip + clamp.
        ResetAllCategories();
        string path = Path.Combine(dir, "steer-max.cfg");
        ModConfig.Load(new ConfigFile(path, true));
        SteeringSettings.Enabled = true;
        SteeringPreset c = SteeringSettings.BeginEdit();
        c.MaxSteerAngle = 52f;
        ModConfig.Save();
        c.MaxSteerAngle = 0f;
        ModConfig.Load(new ConfigFile(path, true));
        Check(Near(SteeringPreset.Custom.MaxSteerAngle, 52f), "MaxSteerAngle round-trips through the config");
        File.WriteAllText(path, File.ReadAllText(path).Replace("MaxSteerAngle = 52", "MaxSteerAngle = 900"));
        ModConfig.Load(new ConfigFile(path, true));
        Check(Near(SteeringPreset.Custom.MaxSteerAngle, Limits.MaxSteerAngleMax), "MaxSteerAngle clamps to the limit");

        // SaveTracker: the newest SaveGameN.es3 wins; a missing directory is null.
        string saves = Path.Combine(dir, "saves");
        Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, "SaveGame1.es3"), "a");
        File.WriteAllText(Path.Combine(saves, "SaveGame2.es3"), "b");
        File.SetLastWriteTimeUtc(Path.Combine(saves, "SaveGame2.es3"), new DateTime(2024, 1, 1));
        File.SetLastWriteTimeUtc(Path.Combine(saves, "SaveGame1.es3"), new DateTime(2026, 1, 1));
        Check(Path.GetFileName(SaveTracker.NewestSave(saves)) == "SaveGame1.es3", "NewestSave: the newest slot wins");
        Check(SaveTracker.NewestSave(Path.Combine(dir, "nope")) == null, "NewestSave: a missing directory is null");

        // NoteLoadedSave: the same slot changes nothing; a switch with ResetOnSaveSwitch resets everything.
        string cfg2 = Path.Combine(dir, "saveswitch.cfg");
        ModConfig.Load(new ConfigFile(cfg2, true));   // ResetOnSaveSwitch defaults to TRUE (0.9.0)
        SteeringSettings.Enabled = true;
        SuspensionSettings.Enabled = true;
        AssistsSettings.Enabled = true;
        ModConfig.NoteLoadedSave("SaveGame2.es3", 0L);
        Check(SteeringSettings.Enabled && SuspensionSettings.Enabled && AssistsSettings.Enabled, "same save slot: nothing changes");
        ModConfig.NoteLoadedSave("SaveGame1.es3", 0L);   // the switch
        Check(!SteeringSettings.Enabled && !SuspensionSettings.Enabled && !AssistsSettings.Enabled,
            "ResetOnSaveSwitch (default true): a save switch turns every category off (cancel the config for the old save)");
        ResetAllCategories();
    }

    private static void TestFrozenInputPick()
    {
        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        TargetSettings.Mode = TargetMode.All;
        TargetSettings.SelectedName = "";

        VehicleController parked = MakeCar(out FakeWheel[] _, 0f);
        VehicleController player = MakeCar(out FakeWheel[] _, 0f);
        parked.gameObject.name = "Parked HB";
        player.gameObject.name = "Player";
        parked.input.Handbrake = 1f;    // frozen at exit: the FSM stopped writing here
        UnityEngine.Object.Registry.Add(parked);
        UnityEngine.Object.Registry.Add(player);
        var tuner = new VehicleTuner();
        tuner.ReapplyNow();
        Time.unscaledTime = 100f;

        // First sample after capture: a frozen value gets a 2 s grace (the player is never
        // driving at capture time — the post-load quiet window — so this self-corrects).
        Check(tuner.FindDrivenVehicle() == parked, "captured frozen input gets the first-sample grace");
        Time.unscaledTime = 104f;
        player.input.Throttle = 1f;    // grace long expired: the frozen handbrake is stale now
        Check(tuner.FindDrivenVehicle() == player, "live throttle beats a parked car's frozen handbrake");

        player.input.Throttle = 0f;    // the player's own FSM keeps writing 0 — hands off
        Time.unscaledTime = 105f;
        Check(tuner.FindDrivenVehicle() == player,
            "hands off: the parked car's frozen input no longer steals the pick (0.6.3: it did)");

        player.Speed = 12f;
        VehicleTuner.TelemetrySample sample;
        Check(tuner.TryGetTelemetry(out sample) && Near(sample.SpeedKmh, 43.2f),
            "the strip shows the player's car, not a parked car's zeros");
        player.Speed = 0f;

        // Switching cars still works: fresh input on the parked car takes the pick (release
        // the handbrake and throttle as separate samples, as the 4 Hz strip would see them).
        Time.unscaledTime = 106f;
        parked.input.Handbrake = 0f;
        tuner.FindDrivenVehicle();
        parked.input.Throttle = 1f;
        Time.unscaledTime = 107f;
        Check(tuner.FindDrivenVehicle() == parked, "fresh input on another car takes the pick (car switch)");

        // Once the hold window expires, an unchanged value is dead even above the dead zone.
        Time.unscaledTime = 200f;
        parked.input.Throttle = 1f;    // unchanged since 107 — frozen mid-drive
        player.input.Throttle = 0f;
        Check(tuner.FindDrivenVehicle() == parked,
            "after the hold window the pick stays on the last-driven car (memory), not the frozen input");

        // A parked car captured WITH frozen input gets the first-sample grace, then goes silent.
        UnityEngine.Object.Registry.Clear();
        VehicleController frozen = MakeCar(out FakeWheel[] _, 0f);
        VehicleController other = MakeCar(out FakeWheel[] _, 0f);
        frozen.gameObject.name = "Frozen Brakes";
        other.gameObject.name = "Other";
        frozen.input.Brakes = 0.5f;    // frozen mid-brake
        UnityEngine.Object.Registry.Add(frozen);
        UnityEngine.Object.Registry.Add(other);
        var t2 = new VehicleTuner();
        t2.ReapplyNow();
        Time.unscaledTime = 500f;
        Check(t2.FindDrivenVehicle() == frozen, "first sample after capture: a frozen value gets the 2 s grace");
        Time.unscaledTime = 504f;
        other.input.Throttle = 1f;     // fresh input on the other car
        Check(t2.FindDrivenVehicle() == other, "after the grace: fresh input wins, the frozen value is silent");
        Time.unscaledTime = 506f;
        other.input.Throttle = 0f;
        Check(t2.FindDrivenVehicle() == other, "and the memory keeps the last genuinely driven car");

        // Liveness gate, pure.
        float prev = 0f;
        bool seen = false;
        float lastChange = -1f;
        bool live;
        Check(VehicleTuner.UpdateInputLiveness(1f, 10f, ref prev, ref seen, ref lastChange, out live) && live,
            "first sample above the dead zone is live");
        Check(!VehicleTuner.UpdateInputLiveness(1f, 11f, ref prev, ref seen, ref lastChange, out live) && live,
            "unchanged within the hold window is live");
        Check(!VehicleTuner.UpdateInputLiveness(1f, 13f, ref prev, ref seen, ref lastChange, out live) && !live,
            "unchanged past the hold window is stale");
        Check(VehicleTuner.UpdateInputLiveness(0.6f, 14f, ref prev, ref seen, ref lastChange, out live) && live,
            "a change above the dead zone is live again");
        Check(VehicleTuner.UpdateInputLiveness(0.01f, 15f, ref prev, ref seen, ref lastChange, out live) && !live,
            "input below the dead zone is never live");

        ResetAllCategories();
        UnityEngine.Object.Registry.Clear();
        Time.unscaledTime = 0f;
    }

    // ================================================================ 0.7.0

    private static VehicleController MakeShiftCar(TransmissionComponent.TransmissionShiftType type, string name)
    {
        VehicleController vc = MakeCar(out FakeWheel[] _, 0f);
        vc.gameObject.name = name;
        TransmissionComponent t = vc.powertrain.transmission;
        t.vehicleController = vc;
        vc.input.vehicleController = vc;
        t.transmissionType = type;
        vc.Speed = 0f;
        t.SimulateForwardStep();   // NWH assigns the delegate for the type (stub mirrors :403-405)
        t.TestRpmPerMps = 27.3f;    // wheel rpm per m/s on a 0.35 m wheel
        return vc;
    }

    /// <summary>Ramp the speed, one physics tick per step; returns the number of gear reversals (up then down or v.v.).</summary>
    private static int Ramp(VehicleController vc, float from, float to, float dv, float throttle, List<int> gears)
    {
        TransmissionComponent t = vc.powertrain.transmission;
        vc.input.Throttle = throttle;
        int reversals = 0, lastDir = 0, last = t.Gear;
        float step = to >= from ? Math.Abs(dv) : -Math.Abs(dv);
        for (float v = from; step > 0f ? v <= to : v >= to; v += step)
        {
            vc.Speed = v;
            t.SimulateForwardStep();
            int g = t.Gear;
            if (gears != null) gears.Add(g);
            if (g != last)
            {
                int dir = g > last ? 1 : -1;
                if (lastDir != 0 && dir != lastDir) reversals++;
                lastDir = dir;
                last = g;
            }
        }
        return reversals;
    }

    private static void TestShiftMath()
    {
        Check(Near(ShiftController.KickdownFactor(0.5f, 1f), 1f) && Near(ShiftController.KickdownFactor(0.9f, 1f), 1.15f)
              && Near(ShiftController.KickdownFactor(0.9f, 2f), 1.3f), "kickdown: x1 below 80% throttle, +15% x scale above");
        Check(Near(ShiftController.UpshiftPoint(2800f, 1f, 0.7f, 900f, 5000f, 1f), 2800f), "upshift = the vehicle's own point on a normal ratio step");
        Check(Near(ShiftController.UpshiftPoint(2800f, 0.9f, 0.7f, 900f, 5000f, 1f), 2520f), "ShiftUpFactor scales it (0.9 -> earlier)");
        Check(Near(ShiftController.UpshiftPoint(2800f, 1f, 0.7f, 900f, 5000f, 1.15f), 3220f), "kickdown raises it (shifts later at full throttle)");
        float wide = ShiftController.UpshiftPoint(2800f, 1f, 0.3f, 900f, 5000f, 1f);
        Check(Near(wide, 990f / (0.9f * 0.3f), 0.5f), "wide step (x0.3): upshift raised so the next gear lands above its downshift floor (" + wide.ToString("0") + ")");
        Check(float.IsPositiveInfinity(ShiftController.UpshiftPoint(2800f, 1f, 0.15f, 900f, 5000f, 1f)), "unreachable next gear (even the limiter lands below idle x1.1): no upshift");
        Check(Near(ShiftController.UpshiftPoint(6000f, 1f, 0.7f, 900f, 5000f, 1f), 4850f), "capped at 97% of the rev limiter");
        Check(Near(ShiftController.UpshiftPoint(500f, 1f, 1f, 900f, 5000f, 1f), 1125f), "never below 1.25 x idle");
        Check(Near(ShiftController.DownshiftPoint(1400f, 1f, 2800f, 0.7f, 900f, 1f), 1400f), "downshift = the vehicle's own point when it is clear of the landing RPM");
        Check(Near(ShiftController.DownshiftPoint(1400f, 1.5f, 2800f, 0.7f, 900f, 1f), 2800f * 0.7f * 0.9f), "downshift capped at 90% of the RPM an upshift lands on");
        Check(Near(ShiftController.DownshiftPoint(500f, 1f, 2800f, 0.7f, 900f, 1f), 990f), "downshift never below 1.1 x idle (NWH's own floor)");

        // The no-hunting property over a grid: every upshift lands above the next gear's downshift
        // point, every downshift lands below the lower gear's upshift point.
        bool ok = true;
        float[] steps = { 0.2f, 0.3f, 0.45f, 0.6f, 0.75f, 0.9f, 0.97f };
        float[] factors = { 0.5f, 0.8f, 1f, 1.2f, 1.5f };
        float[] kicks = { 1f, 1.15f, 1.3f };
        foreach (float st in steps)
            foreach (float uf in factors)
                foreach (float df in factors)
                    foreach (float k in kicks)
                    {
                        float up = ShiftController.UpshiftPoint(2800f, uf, st, 900f, 4700f, k);
                        if (float.IsInfinity(up)) continue;
                        float down = ShiftController.DownshiftPoint(1400f, df, up, st, 900f, k);
                        ok &= up * st > down && down / st < up && up <= 4700f * 0.97f + 0.01f;
                    }
        Check(ok, "no hunting anywhere on a grid of ratio steps x factors x kickdown (land above down, below up)");

        Check(ShiftController.AutoForwardTarget(4, 6, 2000f, 1.5f, 2800f, 1400f) == 1, "creep hold: below 2 m/s a higher gear drops to 1st");
        Check(ShiftController.AutoForwardTarget(1, 6, 9000f, 1.5f, 2800f, 1400f) == 1, "creep hold: no upshift out of 1st below 2 m/s");
        Check(ShiftController.AutoForwardTarget(3, 6, 2900f, 20f, 2800f, 1400f) == 4, "above the upshift point: one gear up");
        Check(ShiftController.AutoForwardTarget(6, 6, 9000f, 20f, 2800f, 1400f) == 6, "top gear never upshifts");
        Check(ShiftController.AutoForwardTarget(3, 6, 1300f, 20f, 2800f, 1400f) == 2, "below the downshift point: one gear down");
        Check(ShiftController.AutoForwardTarget(3, 6, 2000f, 20f, 2800f, 1400f) == 3, "in between: stay (hysteresis band)");
        Check(ShiftController.DnrFromForward(1, 0.2f, 0f, false, false, -999, 0.4f) == 0 && ShiftController.DnrFromForward(1, 0.2f, 0.5f, false, false, -999, 0.4f) == 1
              && ShiftController.DnrFromForward(2, 1f, 0f, false, false, -999, 0.4f) == 2, "drive -> neutral like NWH: stopped and off the throttle");
        Check(ShiftController.DnrFromForward(1, 0.2f, 0f, true, false, -999, 0.4f) == 1 && ShiftController.DnrFromForward(1, 0.2f, 0f, true, true, -999, 0.4f) == 0
              && ShiftController.DnrFromForward(1, 0.2f, 0f, true, false, -1, 0.4f) == -1, "RequireShiftInput: N / R only on the game's shift request");
        Check(ShiftController.DnrFromNeutralOrReverse(0, 0f, 0.3f, 0f, 0.4f) == 1 && ShiftController.DnrFromNeutralOrReverse(0, 0f, 0f, 0.3f, 0.4f) == -1
              && ShiftController.DnrFromNeutralOrReverse(-1, 0.1f, 0f, 0.3f, 0.4f) == 0 && ShiftController.DnrFromNeutralOrReverse(0, 0f, 0f, 0f, 0.4f) == 0,
            "neutral/reverse (Manual-type car in Automatic mode): NWH's Auto DNR rules");
        Check(ShiftController.ManualTarget(3, true, false, -999, 12, 1) == 4 && ShiftController.ManualTarget(12, true, false, -999, 12, 1) == 12
              && ShiftController.ManualTarget(-1, false, true, -999, 12, 1) == -1 && ShiftController.ManualTarget(2, false, false, 9, 5, 1) == 5
              && ShiftController.ManualTarget(2, false, false, -999, 5, 1) == 2 && ShiftController.ManualTarget(4, false, false, 0, 5, 1) == 0,
            "manual requests: up / down / ShiftInto, clamped to the tuned box; no request = stay");
        Check(ShiftController.IsAutomatic(GearboxMode.Stock, TransmissionComponent.TransmissionShiftType.Automatic)
              && !ShiftController.IsAutomatic(GearboxMode.Stock, TransmissionComponent.TransmissionShiftType.Manual)
              && ShiftController.IsAutomatic(GearboxMode.Automatic, TransmissionComponent.TransmissionShiftType.Manual)
              && !ShiftController.IsAutomatic(GearboxMode.Manual, TransmissionComponent.TransmissionShiftType.Automatic),
            "mode Stock follows the vehicle's own type; Manual/Automatic override it");
    }

    private static void TestShiftController()
    {
        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        TargetSettings.Mode = TargetMode.All;
        TransmissionComponent.DeferShifts = false;

        // 1. The 0.6.x "stuck automatic": NWH's own automatic on a wide-step box hunts.
        VehicleController hv = MakeShiftCar(TransmissionComponent.TransmissionShiftType.Automatic, "Hunter");
        TransmissionComponent ht = hv.powertrain.transmission;
        ht.gears = new List<float> { -2.216f, 0f, 3.274f, 0.98f, 0.7f, 0.5f, 0.4f };
        ht.Gear = 1;
        // Sequential (the hunting mechanism on a wide step; gear-skipping is modeled in the
        // 0.7.3 launch test instead).
        ht.allowUpshiftGearSkipping = false;
        hv.Speed = 2900f / (27.3f * 3.274f * 6f);   // 2900 rpm in 1st
        for (int i = 0; i < 100; i++) { ht.SimulateForwardStep(); hv.Speed = 2900f / (27.3f * 3.274f * 6f); }
        Check(ht.NwhAutoShifts > 50, "control: NWH's raw automatic on a x0.3 ratio step hunts 1<->2 (" + ht.NwhAutoShifts + " shifts in 100 ticks; each shift opens the clutch = revs, no drive)");

        UnityEngine.Object.Registry.Add(hv);
        var tuner = new VehicleTuner();
        tuner.ReapplyNow();
        ht.gears = new List<float> { -2.216f, 0f, 3.274f, 2.093f, 1.439f, 1.084f, 0.817f };   // back to stock before capture matters
        UnityEngine.Object.Registry.Clear();
        hv = MakeShiftCar(TransmissionComponent.TransmissionShiftType.Automatic, "Hunter");
        ht = hv.powertrain.transmission;
        UnityEngine.Object.Registry.Add(hv);
        tuner = new VehicleTuner();
        tuner.ReapplyNow();
        TransmissionComponent.Shift nwh = ht.shiftDelegate;
        var gearsBefore = new List<float>(ht.gears);
        GearboxSettings.Enabled = true;
        GearboxPreset p = GearboxSettings.BeginEdit();
        p.SetScale(2, 0.98f / 2.093f);   // the same x0.3 step 1st -> 2nd
        tuner.ApplyLive();
        Check(!ReferenceEquals(ht.shiftDelegate, nwh) && tuner.ShiftControlledCount == 1, "Gearbox ON: the mod's controller is the shift delegate");
        ht.Gear = 1;
        ht.NwhAutoShifts = 0;
        float v1 = 2900f / (27.3f * 3.274f * 6f);
        for (int i = 0; i < 100; i++) { hv.Speed = v1; ht.SimulateForwardStep(); }
        Check(ht.Gear == 1 && ht.NwhAutoShifts == 0, "same box under the controller: holds 1st at 2900 rpm (the 2nd-gear landing would be below idle x1.1)");
        float v2 = 3800f / (27.3f * 3.274f * 6f);
        for (int i = 0; i < 100; i++) { hv.Speed = v2; ht.SimulateForwardStep(); }
        Check(ht.Gear == 2, "at 3800 rpm it upshifts once and stays in 2nd (" + ht.Gear + ")");

        // 2. Restore byte-for-byte.
        GearboxSettings.Enabled = false;
        tuner.ApplyLive();
        bool same = ht.gears.Count == gearsBefore.Count;
        for (int i = 0; same && i < gearsBefore.Count; i++) same &= ht.gears[i] == gearsBefore[i];
        Check(ReferenceEquals(ht.shiftDelegate, nwh) && same && ht.transmissionType == TransmissionComponent.TransmissionShiftType.Automatic,
            "Gearbox OFF: the very same NWH delegate instance, the stock gear list and the type are back");
        Check(ht.Gear >= 0 && ht.Gear <= 5, "OFF leaves the transmission in a valid gear");

        // 3. A 12-gear Truck box on an AUTOMATIC car drives through every gear without hunting.
        UnityEngine.Object.Registry.Clear();
        VehicleController tv = MakeShiftCar(TransmissionComponent.TransmissionShiftType.Automatic, "Rustliner(Clone)2360");
        TransmissionComponent tt = tv.powertrain.transmission;
        UnityEngine.Object.Registry.Add(tv);
        tuner = new VehicleTuner();
        tuner.ReapplyNow();
        GearboxSettings.Enabled = true;
        GearboxSettings.SetPresetByName("Truck");
        tuner.ApplyLive();
        Check(tt.gears.Count == 14 && tuner.ShiftControlledCount == 1, "Truck: 12 forward gears on a 5-speed automatic, controller hooked");
        var seq = new List<int>();
        tv.input.Throttle = 0.6f;
        tv.Speed = 0f;
        tt.SimulateForwardStep();   // N -> 1 through NWH's own drive engagement (stock delegate in N)
        int rev = Ramp(tv, 0f, 60f, 0.02f, 0.6f, seq);
        int maxGear = 0;
        foreach (int g in seq) if (g > maxGear) maxGear = g;
        Check(maxGear == 12 && rev == 0, "accelerating 0-60 m/s: shifts up through all 12 gears in order, never back (" + maxGear + " reached, " + rev + " reversals)");
        var down = new List<int>();
        int rev2 = Ramp(tv, 60f, 0f, 0.02f, 0f, down);
        Check(rev2 == 0 && tt.Gear == 0, "coasting to a stop: downshifts only, then neutral like NWH (gear " + tt.Gear + ", " + rev2 + " reversals)");

        // 4. Manual mode on an automatic car: the game's shift requests, clamped to the tuned box.
        GearboxPreset mp = GearboxSettings.BeginEdit();
        mp.TransmissionMode = GearboxMode.Manual;
        tuner.ApplyLive();
        tv.Speed = 10f;
        tv.input.ShiftInto = 3; tt.SimulateForwardStep();
        bool s3 = tt.Gear == 3;
        tv.input.ShiftUp = true; tt.SimulateForwardStep();
        bool s4 = tt.Gear == 4;
        tv.input.ShiftInto = 11; tt.SimulateForwardStep();
        bool s11 = tt.Gear == 11;
        tv.input.ShiftInto = 40; tt.SimulateForwardStep();
        bool s12 = tt.Gear == 12;
        tt.SimulateForwardStep();
        Check(s3 && s4 && s11 && s12 && tt.Gear == 12, "manual: ShiftInto 3, ShiftUp -> 4, ShiftInto 11 (a gear the game's keys don't have), 40 clamps to 12; no request = stay");
        Check(tv.input.ShiftInto == -999 && !tv.input.ShiftUp, "the requests are consumed by NWH's ResetShiftFlags (the mod never writes vc.input)");

        // 5. The game changes the type while we own the box (CheckTag): NWH re-assigns, we re-hook.
        tt.transmissionType = TransmissionComponent.TransmissionShiftType.Manual;
        tt.SimulateForwardStep();
        Check(tt.HasNwhDelegate, "(stub mirrors NWH) a type change re-assigns NWH's own delegate on the next tick");
        tuner.ApplyLive();
        Check(!tt.HasNwhDelegate && tuner.ShiftControlledCount == 1, "the next pass hooks the controller again");
        GearboxSettings.Enabled = false;
        tuner.ApplyLive();
        Check(tt.HasNwhDelegate && tt.transmissionType == TransmissionComponent.TransmissionShiftType.Manual && tt.gears.Count == 7,
            "OFF after the change: the game's new type and NWH's delegate for it stay (no stale restore)");

        // 5b. 0.7.3: the instant re-hook. The game's auto-gearbox setting flips transmissionType
        // (Auto -> Manual -> Auto): each flip makes NWH re-assign its own delegate, and on the
        // second flip its gear-skipping automatic runs the tuned 12-gear box — from a launch it
        // lands in a tall gear in one ShiftInto (the "launching from gear 8" report). The guard
        // re-installs the controller in the same tick, so the window is zero.
        GearboxSettings.Enabled = true;
        GearboxSettings.SetPresetByName("Truck");
        tuner.ApplyLive();
        Check(tuner.ShiftControlledCount == 1 && !tt.HasNwhDelegate, "Truck hooked again");
        float launchSpeed = 3800f / (27.3f * 3.274f * 6f);   // 3800 rpm (no-slip) in 1st: above the upshift point
        tv.Speed = launchSpeed;
        tt.Gear = 1;
        TransmissionComponent.AfterDelegateReassign = null;   // the window WITHOUT the guard
        tt.transmissionType = TransmissionComponent.TransmissionShiftType.Manual;
        tt.SimulateForwardStep();
        tt.transmissionType = TransmissionComponent.TransmissionShiftType.Automatic;
        tt.SimulateForwardStep();
        int skipped = tt.Gear;
        bool gear8Launch = skipped >= 4;
        tt.Gear = 1;
        TransmissionComponent.AfterDelegateReassign = VehicleTuner.RehookIfControlled;   // the guard (the Harmony postfix)
        tt.transmissionType = TransmissionComponent.TransmissionShiftType.Manual;
        tt.SimulateForwardStep();
        tt.transmissionType = TransmissionComponent.TransmissionShiftType.Automatic;
        tt.SimulateForwardStep();
        bool guarded = !tt.HasNwhDelegate && tt.Gear <= 2;
        TransmissionComponent.AfterDelegateReassign = null;
        tt.Gear = 1;
        Check(gear8Launch, "without the guard, the game's skipping automatic lands the 12-gear box in gear " + skipped + " from a launch (the report)");
        Check(guarded, "with the guard, the controller is back in the same tick and the box shifts one gear at a time (" + tt.Gear + ")");

        // 6. Manual-type car, mode Stock: the vehicle's own ManualShift does the shifting.
        // (Set the type back to Manual: 5b's flips left it Automatic, and the mode follows
        // the live type since 0.7.6.)
        GearboxSettings.Enabled = true;
        tt.transmissionType = TransmissionComponent.TransmissionShiftType.Manual;
        tt.SimulateForwardStep();
        GearboxSettings.BeginEdit().TransmissionMode = GearboxMode.Stock;
        GearboxSettings.Shown.GearCount = 12;
        tuner.ApplyLive();
        tt.Gear = 12;
        tv.input.ShiftUp = true; tt.SimulateForwardStep();
        bool topStays = tt.Gear == 12;
        tv.input.ShiftInto = 2; tt.SimulateForwardStep();
        Check(topStays && tt.Gear == 2 && !tt.HasNwhDelegate, "Manual type + mode Stock: NWH's ManualShift via the controller (12th + ShiftUp stays, ShiftInto 2 works)");

        // 7. A fault inside the controller never escapes into NWH's ForwardStep.
        tt.Gear = 30;   // out of the list: the controller's ratio lookup throws
        bool threw = false;
        try { tt.shiftDelegate(tv); tt.shiftDelegate(tv); } catch (Exception) { threw = true; }
        Check(!threw, "a throwing controller is caught, logged once, and hands shifting to the vehicle's own delegate");
        tt.Gear = 2;

        // 8. A controller left behind by a dead runner is never captured as "stock".
        UnityEngine.Object.Registry.Clear();
        VehicleController sv = MakeShiftCar(TransmissionComponent.TransmissionShiftType.Automatic, "Stale");
        TransmissionComponent st = sv.powertrain.transmission;
        TransmissionComponent.Shift own = st.shiftDelegate;
        UnityEngine.Object.Registry.Add(sv);
        var a = new VehicleTuner();
        GearboxSettings.Enabled = true;
        GearboxSettings.SetPresetByName("Truck");
        a.ReapplyNow();
        // runner A dies without its OnDestroy restore (the worst case); runner B takes over.
        var b = new VehicleTuner();
        b.ReapplyNow();
        GearboxSettings.Enabled = false;
        b.ApplyLive();
        Check(ReferenceEquals(st.shiftDelegate, own), "a stale controller's own captured stock is used: OFF restores NWH's delegate, not the dead runner's");

        // 9. Stock and clutch-only presets keep NWH's own shifting (exactly as shipped).
        UnityEngine.Object.Registry.Clear();
        VehicleController nv = MakeShiftCar(TransmissionComponent.TransmissionShiftType.Automatic, "Plain");
        UnityEngine.Object.Registry.Add(nv);
        var tn = new VehicleTuner();
        GearboxSettings.Enabled = true;
        GearboxSettings.SetPresetByName("Stock");
        tn.ReapplyNow();
        bool stockNwh = nv.powertrain.transmission.HasNwhDelegate;
        GearboxSettings.SetPresetByName("Race");
        tn.ApplyLive();
        bool raceNwh = nv.powertrain.transmission.HasNwhDelegate && Near(nv.powertrain.clutch.slipTorque, 900f);
        GearboxSettings.BeginEdit().ShiftUpFactor = 0.9f;
        tn.ApplyLive();
        bool knobHooks = !nv.powertrain.transmission.HasNwhDelegate;
        GearboxSettings.SetPresetByName("Comfort");
        tn.ApplyLive();
        Check(stockNwh && raceNwh && knobHooks && nv.powertrain.transmission.HasNwhDelegate,
            "Stock / clutch-only presets keep NWH's own shifting; a shift knob, gear change or forced mode hands it to the mod (and back)");
        GearboxSettings.Enabled = false;
        tn.ApplyLive();

        // 10. CVT: never hooked.
        UnityEngine.Object.Registry.Clear();
        VehicleController cv = MakeShiftCar(TransmissionComponent.TransmissionShiftType.CVT, "Cvt");
        cv.powertrain.transmission.gears = new List<float> { -3f, 0f, 2f };
        UnityEngine.Object.Registry.Add(cv);
        var tc = new VehicleTuner();
        GearboxSettings.Enabled = true;
        tc.ReapplyNow();
        Check(cv.powertrain.transmission.HasNwhDelegate && tc.ShiftControlledCount == 0, "CVT boxes keep their own shifting (never hooked)");
        GearboxSettings.Enabled = false;
        tc.ApplyLive();

        // 11. The per-tick path allocates nothing.
        UnityEngine.Object.Registry.Clear();
        VehicleController av = MakeShiftCar(TransmissionComponent.TransmissionShiftType.Automatic, "Alloc");
        TransmissionComponent at = av.powertrain.transmission;
        UnityEngine.Object.Registry.Add(av);
        var tA = new VehicleTuner();
        GearboxSettings.Enabled = true;
        GearboxSettings.SetPresetByName("Truck");
        tA.ReapplyNow();
        at.Gear = 3;
        av.Speed = 15f;
        av.input.Throttle = 0.9f;
        TransmissionComponent.Shift del = at.shiftDelegate;
        del(av);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++) { av.Speed = 10f + (i % 50) * 0.5f; del(av); }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, "the controller's per-tick path allocates nothing (" + bytes + " bytes over 500 ticks)");

        // 12. 0.7.5: enabled AT LOAD (the user report: loading with a custom gearbox on leaves
        // the wheels dead, while enabling it after load works). The load path differs: the first
        // capture happens during the spawn wave, and the game's init FSMs flip transmissionType
        // AFTER the tuner's first apply. Simulate the whole sequence: settings already on before
        // the first scan, a type flip mid-wave (guard re-hooks), then a drive ramp.
        UnityEngine.Object.Registry.Clear();
        VehicleController lv = MakeShiftCar(TransmissionComponent.TransmissionShiftType.Automatic, "LoadCase");
        TransmissionComponent lt = lv.powertrain.transmission;
        UnityEngine.Object.Registry.Add(lv);
        GearboxSettings.Enabled = true;                       // the cfg is on at startup
        GearboxSettings.SetPresetByName("Truck");
        var tL = new VehicleTuner();
        tL.ReapplyNow();                                       // first scan + apply during the wave
        Check(!lt.HasNwhDelegate && tL.ShiftControlledCount == 1, "enabled at load: the controller is hooked on the first apply");
        TransmissionComponent.AfterDelegateReassign = VehicleTuner.RehookIfControlled;
        lt.transmissionType = TransmissionComponent.TransmissionShiftType.Manual;    // the game's setting FSM, mid-wave
        lt.SimulateForwardStep();
        lt.transmissionType = TransmissionComponent.TransmissionShiftType.Automatic;
        lt.SimulateForwardStep();
        TransmissionComponent.AfterDelegateReassign = null;
        Check(!lt.HasNwhDelegate, "after the mid-wave type flips the controller is still the delegate (guard)");
        // The player gets in and launches: N -> 1 through the game's own DNR, then up through the box.
        lv.input.Throttle = 0.8f;
        lv.Speed = 0f;
        lt.SimulateForwardStep();
        Check(lt.Gear == 1, "throttle from neutral engages 1st (" + lt.Gear + ")");
        var loadSeq = new List<int>();
        int loadRev = Ramp(lv, 0f, 60f, 0.02f, 0.8f, loadSeq);
        int loadMax = 0;
        foreach (int g in loadSeq) if (g > loadMax) loadMax = g;
        Check(loadMax == 12 && loadRev == 0, "the load-path box drives up through all 12 gears, never back (" + loadMax + " reached, " + loadRev + " reversals)");
        GearboxSettings.Enabled = false;
        tL.ApplyLive();
        TransmissionComponent.AfterDelegateReassign = null;
        ResetAllCategories();

        // 13. 0.7.6: the load bug. At load the tuner hooks before the game's CheckTag has
        // written the real transmission type, so it captures the prefab default (Manual) on a
        // Stock-mode preset — the controller follows the vehicle's type, runs the game's own
        // ManualShift, and throttle from neutral does nothing forever (the user report: wheels
        // dead after loading with a custom gearbox, fine when enabled in-session). The guard
        // must adopt the game's fresh delegate AND type on the flip.
        UnityEngine.Object.Registry.Clear();
        VehicleController mv = MakeShiftCar(TransmissionComponent.TransmissionShiftType.Manual, "ManualPrefab");
        TransmissionComponent mt = mv.powertrain.transmission;
        UnityEngine.Object.Registry.Add(mv);
        GearboxSettings.Enabled = true;
        GearboxSettings.SetPresetByName("Truck");
        var tM = new VehicleTuner();
        tM.ReapplyNow();                                        // hooks on the prefab default: Manual
        Check(!mt.HasNwhDelegate && mt.transmissionType == TransmissionComponent.TransmissionShiftType.Manual,
            "load: hooked while the prefab type is still Manual");
        mv.input.Throttle = 0.8f;
        mt.SimulateForwardStep();
        bool stuckManual = mt.Gear == 0;
        TransmissionComponent.AfterDelegateReassign = VehicleTuner.RehookIfControlled;
        mt.transmissionType = TransmissionComponent.TransmissionShiftType.Automatic;   // the game's setting FSM
        mt.SimulateForwardStep();
        TransmissionComponent.AfterDelegateReassign = null;
        Check(stuckManual, "before the game's type write, throttle from neutral stays in neutral (the manual path)");
        Check(!mt.HasNwhDelegate, "after the flip the guard still owns the box");
        mv.input.Throttle = 0.8f;
        mt.SimulateForwardStep();
        bool engaged = mt.Gear == 1;
        var fixedSeq = new List<int>();
        int fixedRev = Ramp(mv, 0f, 60f, 0.02f, 0.8f, fixedSeq);
        int fixedMax = 0;
        foreach (int g in fixedSeq) if (g > fixedMax) fixedMax = g;
        Check(engaged, "with the adopted type, throttle from neutral engages 1st (" + mt.Gear + ")");
        Check(fixedMax == 12 && fixedRev == 0, "and the box drives up through all 12 gears (" + fixedMax + ", " + fixedRev + " reversals)");
        GearboxSettings.Enabled = false;
        tM.ApplyLive();
        TransmissionComponent.AfterDelegateReassign = null;
        ResetAllCategories();
        tA.ApplyLive();
        UnityEngine.Object.Registry.Clear();
    }

    private static void TestTruckPreset()
    {
        GearboxPreset tr = GearboxSettings.Book.FindBuiltIn("Truck");
        Check(tr != null && tr == GearboxPreset.Truck && GearboxPreset.Presets.Length == 6 && GearboxPreset.Presets[4] == tr
              && GearboxPreset.Presets[5] == GearboxPreset.Custom, "Truck preset: sixth button before Custom (3 + 3 grid)");
        bool scales = Near(tr.Scale(1), 1.2f) && Near(tr.Scale(11), 0.9f) && Near(tr.Scale(12), 0.85f);
        for (int g = 2; g <= 6; g++) scales &= Near(tr.Scale(g), 1.1f);
        for (int g = 7; g <= 10; g++) scales &= Near(tr.Scale(g), 1f);
        Check(tr.GearCount == 12 && scales, "Truck: 12 gears, 1st +20%, 2-6 +10%, 7-10 stock, 11-12 -10/-15%");
        Check(Near(tr.ClutchGripScale, 1.1f) && Near(tr.ClutchRangeScale, 1.15f) && Near(tr.ShiftUpFactor, 0.9f) && tr.TransmissionMode == GearboxMode.Stock,
            "Truck: heavy-duty clutch (x1.1 / x1.15), earlier upshifts (x0.9), mode Stock");
        float[] stock = { -2.216f, 0f, 3.274f, 2.093f, 1.439f, 1.084f, 0.817f };
        float[] ext = VehicleTuner.ExtendRatios(stock, 1, 5);
        Check(ext[11] / ext[0] < 0.04f, "control: continuing a 5-speed to 12 gears spans " + (ext[0] / ext[11]).ToString("0") + ":1 (12th gear absurdly tall)");
        bool falling = true;
        var spread = new List<float> { stock[0], 0f };
        for (int g = 1; g <= 12; g++)
        {
            float r = VehicleTuner.SpreadRatio(3.274f, 0.817f, g - 1, 12) * tr.Scale(g);
            if (g > 1) falling &= r < spread[spread.Count - 1];
            spread.Add(r);
        }
        float span = spread[2] / spread[13];
        Check(tr.SpreadRatios && falling && span > 4f && span < 8f && Near(spread[2], 3.274f * 1.2f) && Near(spread[13], 0.817f * 0.85f),
            "Truck spreads its 12 gears over the stock 1st..top range: strictly falling, " + span.ToString("0.0") + ":1 overall");
        Check(VehicleTuner.TryStripContinuation(spread, 1) == 0 && VehicleTuner.SpreadRatio(3f, 1f, 0, 12) == 3f && Near(VehicleTuner.SpreadRatio(3f, 1f, 11, 12), 1f),
            "spread ratios are progressive, not geometric: a save baked with them is never mistaken for a continuation by the self-heal");
        GearboxPreset copy = new GearboxPreset { Name = "x" };
        copy.CopyValuesFrom(tr);
        Check(Near(copy.ShiftUpFactor, 0.9f) && copy.GearCount == 12, "CopyValuesFrom carries the shift knobs (preset fork)");
    }

    private static void TestDriftAndCentre()
    {
        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        TargetSettings.Mode = TargetMode.All;
        VehicleController vc = MakeCar(out FakeWheel[] _, 0f);
        UnityEngine.Object.Registry.Add(vc);
        var tuner = new VehicleTuner();
        tuner.ReapplyNow();
        DrivetrainSettings.Enabled = true;
        DrivetrainPreset dp = DrivetrainSettings.BeginEdit();
        dp.PowerScale = 2f;
        dp.FinalDriveScale = 1.5f;
        tuner.ApplyLive();
        Check(Near(vc.powertrain.engine.maxPower, 240f) && Near(vc.powertrain.transmission.finalGearRatio, 9f), "drivetrain applied (x2 power, x1.5 final drive)");
        vc.powertrain.engine.maxPower = 150f;                 // an engine swap while tuned
        vc.powertrain.transmission.finalGearRatio = 4f;       // the game's CheckTag FSM
        tuner.ApplyLive();
        Check(Near(vc.powertrain.engine.maxPower, 300f) && Near(vc.powertrain.transmission.finalGearRatio, 6f),
            "a value the game changed while tuned becomes the new stock (x2 of 150, x1.5 of 4), not overwritten from the stale baseline");
        DrivetrainSettings.Enabled = false;
        tuner.ApplyLive();
        Check(Near(vc.powertrain.engine.maxPower, 150f) && Near(vc.powertrain.transmission.finalGearRatio, 4f),
            "OFF restores the game's new values (150 / 4), not the first-sight ones (120 / 6)");
        DrivetrainSettings.Enabled = true;
        tuner.ApplyLive();
        vc.powertrain.transmission.UpshiftRPM = 3300f;        // CheckTag again
        tuner.ApplyLive();
        DrivetrainSettings.Enabled = false;
        tuner.ApplyLive();
        Check(Near(vc.powertrain.transmission.UpshiftRPM, 3300f), "shift RPMs written by the game survive OFF too");

        ClutchComponent cl = vc.powertrain.clutch;
        GearboxSettings.Enabled = true;
        GearboxSettings.SetPresetByName("Race");
        tuner.ApplyLive();
        cl.slipTorque = 800f;                                  // engine swap re-sizes the clutch
        tuner.ApplyLive();
        Check(Near(cl.slipTorque, 800f * 1.8f), "clutch: a game-changed slip torque becomes the new stock (x1.8 of 800)");
        GearboxSettings.Enabled = false;
        tuner.ApplyLive();
        Check(Near(cl.slipTorque, 800f), "clutch OFF restores the game's 800, not the first-sight 500");

        // Centre diff mode + torque split (layout rigs: NWH-style wiring).
        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        LayoutRig awd = MakeLayoutCar(2, true, "Awd");
        LayoutRig rwd = MakeLayoutCar(2, false, "Rwd");
        UnityEngine.Object.Registry.Add(awd.Vc);
        UnityEngine.Object.Registry.Add(rwd.Vc);
        var t2 = new VehicleTuner();
        t2.ReapplyNow();
        Check(t2.HasCentreDiff(awd.Vc) && !t2.HasCentreDiff(rwd.Vc), "centre diff detected on the AWD car only (the panel enables the bias slider for it)");
        float[] shares = new float[VehicleTuner.MaxAxles];
        int axles, driven;
        Check(t2.TryGetDriveSplit(awd.Vc, shares, out axles, out driven) && axles == 2 && driven == 2 && Near(shares[0], 0.5f) && Near(shares[1], 0.5f),
            "AWD with an LSD centre: nominal 50 / 50");
        Check(t2.TryGetDriveSplit(rwd.Vc, shares, out axles, out driven) && driven == 1 && Near(shares[1], 1f) && Near(shares[0], 0f),
            "RWD: one driven axle (the panel shows the 'drives one axle - use a layout' note)");
        DrivetrainSettings.Enabled = true;
        DrivetrainPreset cp = DrivetrainSettings.BeginEdit();
        cp.DiffCenterMode = DiffMode.Locked;
        t2.ApplyLive();
        Check(awd.Centre.DifferentialType == DifferentialComponent.Type.Locked && awd.AxleDiffs[0].DifferentialType == DifferentialComponent.Type.Open
              && awd.AxleDiffs[1].DifferentialType == DifferentialComponent.Type.Open, "DiffCenterMode Locked: the centre diff locks, axle diffs keep Stock");
        cp.DiffCenterMode = DiffMode.Open;
        cp.DiffBiasScale = 1.5f;
        t2.ApplyLive();
        t2.TryGetDriveSplit(awd.Vc, shares, out axles, out driven);
        Check(awd.Centre.DifferentialType == DifferentialComponent.Type.Open && Near(awd.Centre.biasAB, 0.6f) && Near(shares[0], 0.4f) && Near(shares[1], 0.6f),
            "Open centre with bias x1.5 (0.4 -> 0.6): readout front 40% / rear 60%");
        DrivetrainSettings.LayoutEnabled = true;
        DrivetrainSettings.LayoutText = DrivetrainSettings.LayoutTemplates[2];   // AWD template (split 0.4 to the front)
        t2.ApplyLive();
        t2.TryGetDriveSplit(rwd.Vc, shares, out axles, out driven);
        Check(driven == 2 && Near(shares[0], 0.4f) && Near(shares[1], 0.6f), "RWD car with the AWD layout template: the readout follows the live wiring (40 / 60)");
        DrivetrainSettings.Enabled = false;
        t2.ApplyLive();
        Check(awd.Centre.DifferentialType == DifferentialComponent.Type.LimitedSlip && Near(awd.Centre.biasAB, 0.4f), "OFF restores the centre diff (LSD, bias 0.4)");
        bool templatesOk = DrivetrainSettings.LayoutTemplates.Length == DrivetrainSettings.LayoutTemplateNames.Length;
        foreach (string text in DrivetrainSettings.LayoutTemplates)
        {
            DrivetrainLayout l; string err;
            templatesOk &= DrivetrainLayout.TryParse(text, out l, out err);
        }
        DrivetrainSettings.LayoutText = DrivetrainSettings.LayoutTemplates[3];
        Check(templatesOk && DrivetrainSettings.LayoutTemplateIndex() == 3, "every panel layout template parses; the active one is recognised");
        DrivetrainSettings.LayoutText = DrivetrainSettings.DefaultLayoutText;
        ResetAllCategories();
        UnityEngine.Object.Registry.Clear();
    }

    private static void TestTelemetryCells(string dir)
    {
        ResetAllCategories();
        Check(TelemetryCells.DefaultText == "Speed;Rpm;Gear;SlipFront", "the default strip shows speed, RPM, gear and front slip");
        Check(TelemetryCells.Parse("latg", TelemetryCell.Speed) == TelemetryCell.LatG && TelemetryCells.Parse("Bogus", TelemetryCell.Speed) == TelemetryCell.Speed,
            "cell names parse case-insensitive; unknown falls back");
        Check(TelemetryCells.All.Length == 10, "ten readouts available");
        int dropped = TelemetryCells.Load("Speed; Bogus ;Rpm;Speed;;SlipFront");
        Check(TelemetryCells.Count == 3 && dropped == 2 && TelemetryCells.Serialize() == "Speed;Rpm;SlipFront",
            "load drops unknown names and duplicates, keeps order; serialize round-trips");
        bool okToggle = true;
        for (int i = 0; i < 8; i++) okToggle &= TelemetryCells.Set((TelemetryCell)i, true);
        Check(okToggle && !TelemetryCells.Set(TelemetryCell.Brakes, true) && TelemetryCells.Count == TelemetryCells.MaxCells,
            "at most " + TelemetryCells.MaxCells + " cells: the 9th is refused");
        Check(TelemetryCells.Set(TelemetryCell.Speed, false) && !TelemetryCells.IsOn(TelemetryCell.Speed) && TelemetryCells.Count == 7,
            "turning a cell off removes it");
        Check(TelemetryCells.Label(TelemetryCell.Rpm) == "Engine RPM" && TelemetryCells.Label(TelemetryCell.Brakes) == "Brakes", "cell labels");
        var s = new VehicleTuner.TelemetrySample();
        s.SpeedKmh = 132.4f; s.Rpm = 2450f; s.Gear = "3"; s.FrontSlip = 2.14f; s.RearSlip = 1.8f;
        s.LatG = 0.42f; s.LongG = 0.31f; s.SteeringDeg = 12f; s.Throttle = 0.85f; s.Brakes = 0.3f;
        Check(TelemetryStrip.CellText(TelemetryCell.Speed, s) == "132 km/h" && TelemetryStrip.CellText(TelemetryCell.Rpm, s) == "2450 rpm"
              && TelemetryStrip.CellText(TelemetryCell.Gear, s) == "Gear 3" && TelemetryStrip.CellText(TelemetryCell.SlipFront, s) == "SlipF 2.1°"
              && TelemetryStrip.CellText(TelemetryCell.SlipRear, s) == "SlipR 1.8°" && TelemetryStrip.CellText(TelemetryCell.LatG, s) == "Lat 0.42g"
              && TelemetryStrip.CellText(TelemetryCell.LongG, s) == "Long 0.31g" && TelemetryStrip.CellText(TelemetryCell.Steering, s) == "Steer 12°"
              && TelemetryStrip.CellText(TelemetryCell.Throttle, s) == "Thr 85%" && TelemetryStrip.CellText(TelemetryCell.Brakes, s) == "Brk 30%",
            "cell value formatting");

        // Strip geometry: one 32 px row per up-to-six cells; every cell inside the strip, no overlaps.
        bool geo = Near(TelemetryStrip.StripHeight(0), 32f) && Near(TelemetryStrip.StripHeight(6), 32f) && Near(TelemetryStrip.StripHeight(8), 64f);
        for (int count = 1; count <= 8; count++)
        {
            for (int i = 0; i < count; i++)
            {
                PanelLayout.Band bi = TelemetryStrip.CellBand(count, i);
                geo &= bi.Inside(TelemetryStrip.Width, TelemetryStrip.StripHeight(count));
                for (int j = 0; j < i; j++) geo &= !bi.Overlaps(TelemetryStrip.CellBand(count, j));
            }
        }
        Check(geo, "strip rows: up to six per row, a second row beyond; every cell inside the strip, none overlapping");

        // Corner mapping: every mapped cell stays inside the strip at every corner and count.
        bool cornerMap = true;
        foreach (TelemetryCorner c in new[] { TelemetryCorner.TopLeft, TelemetryCorner.TopRight, TelemetryCorner.BottomLeft, TelemetryCorner.BottomRight })
        {
            bool top = TelemetryStrip.CornerAnchor(c).y > 0.5f;
            for (int count = 1; count <= 8 && cornerMap; count++)
            {
                float h = TelemetryStrip.StripHeight(count);
                for (int i = 0; i < count && cornerMap; i++)
                {
                    PanelLayout.Band b = TelemetryStrip.CellBand(count, i);
                    Vector2 p = TelemetryStrip.CellAnchoredPosition(c, count, i, h);
                    float near = top ? -p.y : p.y;
                    float far = top ? -p.y + b.H : p.y + b.H;
                    // 0.01 px epsilon: the per-cell width rounds, so the last cell can exceed by 3e-5 px.
                    bool okCell = near >= 0f && far <= h && p.x >= 0f && p.x + b.W <= TelemetryStrip.Width + 0.01f;
                    cornerMap &= okCell;
                }
            }
        }
        Check(cornerMap, "corner mapping: every cell stays inside the strip at every corner and count");

        // Config round trip.
        string path = Path.Combine(dir, "cells.cfg");
        ModConfig.Load(new ConfigFile(path, true));
        TelemetryCells.Load("Speed;LatG;LongG");
        ModConfig.Save();
        TelemetryCells.Load(TelemetryCells.DefaultText);
        ModConfig.Load(new ConfigFile(path, true));
        Check(TelemetryCells.Serialize() == "Speed;LatG;LongG", "[Telemetry] Cells round-trips through the config");
        File.WriteAllText(path, File.ReadAllText(path).Replace("Cells = Speed;LatG;LongG", "Cells = Nope;Speed"));
        ModConfig.Load(new ConfigFile(path, true));
        Check(TelemetryCells.Serialize() == "Speed" && File.ReadAllText(path).Contains("Cells = Speed")
              && !File.ReadAllText(path).Contains("Nope"), "unknown cell names are dropped on load (runtime and file)");
        TelemetryCells.Load(TelemetryCells.DefaultText);
        ResetAllCategories();
    }

    private static void TestLayout070()
    {
        bool ok = true;
        foreach (float W in new[] { 300f, 400f, 460f, 800f, 1000f })
        {
            float c = PanelLayout.ContentWidth(W);
            bool rowOk = true;
            foreach (bool readout in new[] { false, true })
                {
                    PanelLayout.SliderGeom g = PanelLayout.SliderRow(c, readout);
                    rowOk &= g.Title.Inside(c, g.RowHeight) && g.Hint.Inside(c, g.RowHeight) && g.Slider.Inside(c, g.RowHeight)
                             && g.Value.Inside(c, g.RowHeight) && g.Reset.Inside(c, g.RowHeight);
                    rowOk &= NoOverlap(g.Title, g.Slider, g.Value, g.Reset) && NoOverlap(g.Hint, g.Slider, g.Value, g.Reset);
                    rowOk &= g.Slider.W >= 80f && g.Title.W >= 100f;
                }
            PanelLayout.SwitchRowGeom o = PanelLayout.OptionRow(c), m = PanelLayout.MasterRow(c);
            rowOk &= o.Title.Right <= c - o.SwitchRight - o.SwitchW && o.Hint.Right <= c - o.SwitchRight - o.SwitchW && o.Hint.Bottom <= o.RowHeight;
            rowOk &= m.Title.Right <= c - m.SwitchRight - m.SwitchW && m.Hint.Right <= c - m.SwitchRight - m.SwitchW && m.Hint.Bottom <= m.RowHeight;
            rowOk &= o.SwitchH <= o.RowHeight && m.SwitchH <= m.RowHeight;
            // Tabs: every (bold) name fits its tab at the chosen font; the strip ends above the page.
            int tf = PanelLayout.TabFont(W, SettingsPanel.TabNames);
            float tabW = PanelLayout.TabWidth(W);
            foreach (string n in SettingsPanel.TabNames) rowOk &= PanelLayout.TextWidth(n, tf, true) <= tabW - 2f;
            rowOk &= tf >= 10 && PanelLayout.PageTop(W, SettingsPanel.TabCount) >= PanelLayout.TabsTop + PanelLayout.TabsHeight(W, SettingsPanel.TabCount);
            // Footer bands inside the 110 px footer and apart.
            var st = new PanelLayout.Band(0f, PanelLayout.FooterHeight - PanelLayout.FooterStatusBottom - PanelLayout.FooterStatusHeight, 10f, PanelLayout.FooterStatusHeight);
            var r1 = new PanelLayout.Band(0f, PanelLayout.FooterHeight - PanelLayout.FooterRow1Bottom - PanelLayout.FooterRow1Height, 10f, PanelLayout.FooterRow1Height);
            var r2 = new PanelLayout.Band(0f, PanelLayout.FooterHeight - PanelLayout.FooterRow2Bottom - PanelLayout.FooterRow2Height, 10f, PanelLayout.FooterRow2Height);
            rowOk &= st.Inside(10f, PanelLayout.FooterHeight) && r1.Inside(10f, PanelLayout.FooterHeight) && r2.Inside(10f, PanelLayout.FooterHeight)
                     && !st.Overlaps(r1) && !r1.Overlaps(r2);
            // Preset buttons: the longest label of every book fits half a narrow row at its fitted font (>= 11 px).
            int perRow = PanelLayout.PresetsPerRow(c);
            float btnW = (c - (perRow - 1) * PanelLayout.PresetGap) / perRow - 8f;
            foreach (string label in new[] { "Custom (Euro Truck)", "Custom (Off-road)", "Custom (Comfort)", "Custom (Standard)" })
                rowOk &= PanelLayout.TextWidth(label, PanelLayout.FitFont(label, PanelLayout.PresetFont(c), btnW, true), true) <= btnW;
            CurveEditor.Bands cb = CurveEditor.ComputeBands(c, 330);
            rowOk &= CurveEditor.HintTop + cb.HintHeight <= cb.GraphTop && cb.ReadoutWidth >= CurveEditor.ReadoutMinWidth;
            GearGraph.Bands gb = GearGraph.ComputeBands(c, 110);
            rowOk &= GearGraph.HintTop + gb.HintHeight <= gb.GraphTop && gb.ReadoutWidth >= GearGraph.ReadoutMinWidth;
            Check(rowOk, "layout at " + W + " px (with pins): rows, switches, tabs (font " + tf + "), footer, presets, curve editors and gear graph fit");
            ok &= rowOk;
        }
        Check(Near(PanelLayout.RowHeight, 50f) && Near(PanelLayout.PresetButtonHeight, 40f) && Near(PanelLayout.MasterRowHeight, 66f)
              && Near(PanelLayout.SectionTitleHeight, 32f) && Near(PanelLayout.FooterHeight, 110f), "§5 tighter blocks: 50 / 40 / 66 / 32 / 110 (0.6.x 58 / 44 / 76 / 38 / 124)");
        Check(PanelLayout.TitleFontWide == 19 && PanelLayout.HintFont == 14 && PanelLayout.MasterTitleFontWide == 23 && PanelLayout.MasterHintFontWide == 17
              && PanelLayout.NoteFont(13) == 14, "§5 bigger text: 17->19, 13->14, 21->23, 15->17, notes +1");
        Check(PanelLayout.SliderRow(PanelLayout.ContentWidth(460f), true).RowHeight < 100f && PanelLayout.SliderRow(PanelLayout.ContentWidth(800f), false).RowHeight < 58f,
            "rows are visibly tighter at the same widths (stacked 86 < 100, wide 50 < 58)");
        Check(Near(PanelLayout.EffectiveWidth(300f, 1920f, 1080f, 1f), 300f), "a 300 px PanelWidth renders 300 px (0.6.x silently used 320)");
        Check(PanelLayout.TabsPerRow(300f) == 4 && PanelLayout.TabsPerRow(460f) == 5, "three tab rows below 360 px, two above");
        Check(PanelLayout.TabFont(460f, SettingsPanel.TabNames) <= 14, "the default 460 px window does not use a tab font that overflows 'Suspension'");
        Check(PanelLayout.FitFont("Track (outward) front right", 18, 120f, true) < 18 && PanelLayout.FitFont("Grip", 18, 120f, true) == 18,
            "long slider titles shrink to their band, short ones keep the full size");
        Check(Near(PanelLayout.TextWidth("Suspension", 10, true), 56.68f, 0.05f), "Arial Bold metrics (Suspension = 5.668 em)");
    }

    private static void TestConfig070(string dir)
    {
        string src = Path.Combine("tests", "fixtures", "v064.cfg");
        string path = Path.Combine(dir, "v064.cfg");
        File.Copy(src, path, true);
        var oldKeys = ReadKeys(File.ReadAllText(path));
        ModConfig.Load(new ConfigFile(path, true));
        Check(SteeringSettings.Enabled && SteeringSettings.ActivePreset.Name == "Euro Truck", "0.6.4 cfg: steering kept");
        Check(SuspensionSettings.Enabled && SuspensionSettings.ActivePreset == SuspensionPreset.Custom && Near(SuspensionPreset.Custom.SpringFront, 1.7f),
            "0.6.4 cfg: suspension Custom kept");
        Check(DrivetrainSettings.Enabled && Near(DrivetrainPreset.Custom.PowerScale, 1.4f) && DrivetrainPreset.Custom.DiffRearMode == DiffMode.Locked
              && DrivetrainPreset.Custom.DiffCenterMode == DiffMode.Stock && DrivetrainSettings.LayoutEnabled
              && DrivetrainSettings.LayoutText == "gearbox -> rear; rear: LSD -> RL, RR", "0.6.4 cfg: drivetrain + layout kept, new centre mode = Stock");
        GearboxPreset gc = GearboxPreset.Custom;
        Check(GearboxSettings.Enabled && GearboxSettings.ActivePreset == gc && gc.GearCount == 7 && Near(gc.Scale(2), 1.1f) && gc.TransmissionMode == GearboxMode.Manual
              && Near(gc.ShiftUpFactor, 1f) && Near(gc.ShiftDownFactor, 1f) && Near(gc.KickdownScale, 1f) && !gc.SpreadRatios,
            "0.6.4 cfg: gearbox kept (now live: the gate is gone), new shift knobs at their neutral 1");
        Check(AlignmentSettings.Enabled && UiSettings.TelemetryEnabled && Near(UiSettings.PanelWidth, 300f) && UiSettings.LastTab == 8
              && TargetSettings.Mode == TargetMode.Selected && TargetSettings.SelectedName == "Duke(Clone)6792"
              && TelemetryCells.Serialize() == TelemetryCells.DefaultText,
            "0.6.4 cfg: alignment, UI, telemetry, target kept; the new [Telemetry] Cells key defaults to Speed;Rpm;Gear;SlipFront");
        ModConfig.Save();
        string after = File.ReadAllText(path);
        var newKeys = ReadKeys(after);
        bool kept = true;
        foreach (KeyValuePair<string, string> kv in oldKeys)
        {
            string nv;
            if (!newKeys.TryGetValue(kv.Key, out nv) || nv != kv.Value)
            {
                kept = false;
                Console.WriteLine("  changed/missing: " + kv.Key + " = " + kv.Value + " -> " + (nv ?? "(missing)"));
            }
        }
        Check(kept && oldKeys.Count == 133, "all 133 keys of a real 0.6.4 file survive load + save with their values (no rename, removal or default change)");
        string[] added = { "Drivetrain.Custom|DiffCenterMode", "Gearbox.Custom|SpreadRatios", "Gearbox.Custom|ShiftUpFactor", "Gearbox.Custom|ShiftDownFactor",
            "Gearbox.Custom|KickdownScale", "Telemetry|Cells", "Gearbox|DebugHooks", "Steering.Custom|MaxSteerAngle",
            "General|ResetOnSaveSwitch", "General|LastSave", "General|LastSaveStamp", "PerVehicle|Tunes",
            "Weight|Enabled", "Weight|Preset", "Weight.Custom|BasedOn", "Weight.Custom|FrontKg", "Weight.Custom|RearKg" };
        bool all = newKeys.Count == oldKeys.Count + added.Length;
        foreach (string k in added) all &= newKeys.ContainsKey(k);
        if (!all) foreach (string k in newKeys.Keys) if (!oldKeys.ContainsKey(k)) Console.WriteLine("  new key: " + k);
        Check(all, "exactly seventeen keys added (0.8.0 adds MaxSteerAngle, ResetOnSaveSwitch, LastSave, LastSaveStamp; 0.9.0 adds PerVehicle|Tunes; 0.10.0 adds the five Weight keys)");
        File.WriteAllText(path, after.Replace("ShiftUpFactor = 1", "ShiftUpFactor = 9").Replace("KickdownScale = 1", "KickdownScale = 0.1").Replace("DiffCenterMode = Stock", "DiffCenterMode = 7"));
        ModConfig.Load(new ConfigFile(path, true));
        Check(Near(GearboxPreset.Custom.ShiftUpFactor, Limits.ShiftFactorMax) && Near(GearboxPreset.Custom.KickdownScale, Limits.KickdownMin)
              && DrivetrainPreset.Custom.DiffCenterMode == DiffMode.Stock, "new keys clamp to Limits; a numeric DiffCenterMode falls back to Stock");
        GearboxPreset tmp = new GearboxPreset { Name = "t" };
        string based;
        PresetCodec.Result r = PresetCodec.ParseInto(PresetCategory.Gearbox, "AVT1|Gearbox|Custom|BasedOn=Truck|ShiftUpFactor=0.7|KickdownScale=1.5", tmp, out based);
        Check(r.Ok && r.Applied == 2 && Near(tmp.ShiftUpFactor, 0.7f) && Near(tmp.KickdownScale, 1.5f), "copy/paste carries the new gearbox keys");
        ResetAllCategories();
        TargetSettings.Mode = TargetMode.All;
        UiSettings.TelemetryEnabled = false;
        UiSettings.PanelWidth = Limits.PanelWidthDefault;
        DrivetrainSettings.LayoutText = DrivetrainSettings.DefaultLayoutText;
    }

    private static Dictionary<string, string> ReadKeys(string text)
    {
        var d = new Dictionary<string, string>();
        string section = "";
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
            if (line.Length == 0 || line.StartsWith("#")) continue;
            int eq = line.IndexOf(" =", StringComparison.Ordinal);
            if (eq > 0) d[section + "|" + line.Substring(0, eq)] = line.Substring(eq + 2).Trim();
        }
        return d;
    }

    // ================================================================ 0.9.0

    /// <summary>The physical file line holding [PerVehicle] Tunes (BepInEx escapes the blob onto one line).</summary>
    private static string BlobLine(string text)
    {
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("Tunes = ", StringComparison.Ordinal))
            {
                return line;
            }
        }
        return "";
    }

    private static void TestPerVehicleTunes(string dir)
    {
        string path = Path.Combine(dir, "pvt.cfg");
        if (File.Exists(path)) File.Delete(path);

        // (a) PresetBook per-vehicle semantics, straight on the steering book.
        PresetBook<SteeringPreset> book = SteeringSettings.Book;
        book.ClearVehicles();
        Check(book.VehicleNames.Count == 0 && !book.HasVehicle("Duke(Clone)1"), "per-vehicle book starts empty");
        Check(book.ForVehicle("Duke(Clone)1") == (book.Active ?? book.Identity), "ForVehicle falls back to the global active preset");
        book.SetByName("Euro Truck");
        Check(book.SaveVehicle("Duke(Clone)1") && book.HasVehicle("Duke(Clone)1"), "SaveVehicle stores a copy under the vehicle name");
        SteeringPreset unit = book.VehicleCopy("Duke(Clone)1");
        Check(unit != null && unit != book.Active && Near(unit.RateMultiplier, 0.5f) && unit.BasedOn == "Euro Truck",
            "the copy is a snapshot of the built-in, carrying the origin it forked from");
        Check(!book.SaveVehicle(""), "SaveVehicle rejects an empty vehicle name");
        book.RemoveVehicle("Duke(Clone)1");
        Check(!book.HasVehicle("Duke(Clone)1") && !book.RemoveVehicle("Duke(Clone)1"), "RemoveVehicle drops the tune once");
        book.SetByName("Vanilla");

        // (b) Save -> file: the blob is one escaped physical line carrying every tune.
        ModConfig.Load(new ConfigFile(path, true));
        book.SetByName("Euro Truck");
        book.SaveVehicle("Duke(Clone)1");
        book.SetByName("Custom");
        SteeringPreset.Custom.BasedOn = "Euro Truck";
        SteeringPreset.Custom.RateMultiplier = 1.7f;
        book.SaveVehicle("Van(Clone)2");
        GearboxSettings.Book.SetByName("Custom");
        GearboxPreset.Custom.BasedOn = "Truck";
        GearboxPreset.Custom.KickdownScale = 1.4f;
        GearboxSettings.Book.SaveVehicle("Duke(Clone)1");
        DrivetrainSettings.Book.SetByName("Custom");
        DrivetrainPreset.Custom.BasedOn = "";
        DrivetrainPreset.Custom.PowerScale = 2.2f;
        DrivetrainSettings.Book.SaveVehicle("Duke(Clone)1");
        ModConfig.Save();
        string after = File.ReadAllText(path);
        string blob = BlobLine(after);
        Check(blob.Length > 0 && blob.Contains("Duke(Clone)1|Steering|AVT1|Steering|Custom|BasedOn=Euro Truck"),
            "the [PerVehicle] Tunes blob carries vehicle, category and the built-in origin");
        Check(blob.Contains("Van(Clone)2|Steering|AVT1|Steering|Custom|BasedOn=Euro Truck") && blob.Contains("RateMultiplier=1.7"),
            "a Custom-forked tune exports its own BasedOn, not an empty name");
        Check(blob.Contains("Duke(Clone)1|Gearbox|AVT1|Gearbox|Custom|BasedOn=Truck"),
            "a gearbox tune for the same car sits beside the steering one");
        Check(blob.Contains("Duke(Clone)1|Drivetrain|AVT1|Drivetrain|Custom|BasedOn=|BoostScale=") && blob.Contains("PowerScale=2.2"),
            "an identity-sourced tune exports an empty origin");
        Check(!after.Contains("\nDuke(Clone)1|Gearbox"), "the multi-line blob is escaped onto one physical line by BepInEx");

        // (c) Reload: every book repopulates, values and origins survive.
        ModConfig.Load(new ConfigFile(path, true));
        SteeringPreset sd = SteeringSettings.Book.VehicleCopy("Duke(Clone)1");
        SteeringPreset sv = SteeringSettings.Book.VehicleCopy("Van(Clone)2");
        Check(sd != null && Near(sd.RateMultiplier, 0.5f) && Near(sd.SmoothingScale, 1.7f) && sd.BasedOn == "Euro Truck"
              && sv != null && Near(sv.RateMultiplier, 1.7f) && sv.BasedOn == "Euro Truck",
            "reload: steering tunes keep their values and origin");
        GearboxPreset gd = GearboxSettings.Book.VehicleCopy("Duke(Clone)1");
        DrivetrainPreset dd = DrivetrainSettings.Book.VehicleCopy("Duke(Clone)1");
        Check(gd != null && Near(gd.KickdownScale, 1.4f) && gd.BasedOn == "Truck"
              && dd != null && Near(dd.PowerScale, 2.2f) && dd.BasedOn == "",
            "reload: gearbox + drivetrain tunes for the same car keep values and origin");
        Check(SteeringSettings.Book.VehicleNames.Count == 2 && GearboxSettings.Book.VehicleNames.Count == 1
              && DrivetrainSettings.Book.VehicleNames.Count == 1,
            "each book keeps only its own tunes");
        Check(!SuspensionSettings.Book.HasVehicle("Duke(Clone)1") && !AeroSettings.Book.HasVehicle("Duke(Clone)1"),
            "a tune never leaks into another category's book");

        // (d) A second save reproduces the blob byte-for-byte (round trip is stable).
        ModConfig.Save();
        Check(BlobLine(File.ReadAllText(path)) == blob, "a second save reproduces the blob byte-for-byte (idempotent)");

        // (e) External edit (Apocasetter): junk lines are skipped, valid ones import.
        ConfigFile f = new ConfigFile(path, true);
        string live = f.Bind<string>("PerVehicle", "Tunes", "", "blob").Value;
        string[] junk = {
            "no pipes at all",
            "|Steering|AVT1|Steering|Custom|BasedOn=Euro Truck",
            "Car|Bogus|AVT1|Steering|Custom|BasedOn=Euro Truck",
            "Car|Steering|BOGUS|Steering|Custom|BasedOn=Euro Truck",
            "Car|Steering|AVT1|Suspension|Custom|BasedOn=Stock",
            "Car2|Gearbox|AVT1|Gearbox|Custom|BasedOn=Stock|Gear3Scale=not-a-float"
        };
        f.Bind<string>("PerVehicle", "Tunes", "", "blob").Value = live + "\n" + string.Join("\n", junk)
            + "\nBus(Clone)3|Steering|AVT1|Steering|Custom|BasedOn=Euro Truck|SmoothingScale=1.3";
        f.Save();
        ModConfig.Load(new ConfigFile(path, true));
        Check(SteeringSettings.Book.HasVehicle("Duke(Clone)1") && SteeringSettings.Book.HasVehicle("Van(Clone)2")
              && !SteeringSettings.Book.HasVehicle("Car"),
            "junk lines (empty name, unknown category, wrong tag, wrong category) are skipped; valid tunes survive");
        GearboxPreset c2 = GearboxSettings.Book.VehicleCopy("Car2");
        Check(c2 != null && Near(c2.Scale(3), 1f) && c2.BasedOn == "",
            "a line with an unparsable key still imports, keeping defaults (identity origin dropped)");
        SteeringPreset bus = SteeringSettings.Book.VehicleCopy("Bus(Clone)3");
        Check(bus != null && Near(bus.SmoothingScale, 1.3f) && bus.BasedOn == "Euro Truck", "a valid appended line imports");
        SteeringPreset d2 = SteeringSettings.Book.VehicleCopy("Duke(Clone)1");
        Check(d2 != null && Near(d2.RateMultiplier, 0.5f), "the original tunes are untouched by the edit");

        // Cleanup so nothing leaks into later tests.
        SteeringSettings.Book.ClearVehicles();
        SuspensionSettings.Book.ClearVehicles();
        AeroSettings.Book.ClearVehicles();
        BrakesSettings.Book.ClearVehicles();
        GripSettings.Book.ClearVehicles();
        DrivetrainSettings.Book.ClearVehicles();
        AssistsSettings.Book.ClearVehicles();
        AlignmentSettings.Book.ClearVehicles();
        GearboxSettings.Book.ClearVehicles();
        ResetAllCategories();
    }

    private static void TestAudit070()
    {
        UiSettings.TelemetryEnabled = true;
        UiSettings.TelemetryScale = 2f;
        UiSettings.TelemetryPosition = TelemetryCorner.BottomRight;
        UiSettings.ResetTelemetry();
        Check(!UiSettings.TelemetryEnabled && Near(UiSettings.TelemetryScale, 1f) && UiSettings.TelemetryPosition == TelemetryCorner.TopLeft,
            "'Reset panel settings' resets telemetry to the shipped defaults (off, x1, top-left), not on / bottom-left");
        var names = new List<string> { "Duke(Clone)6792", "Outrider(Clone)6749" };
        Check(TargetSettings.ResolveSelection("Rustliner(Clone)2360", names, 0) == "Rustliner(Clone)2360",
            "a selected vehicle that has not spawned yet stays selected (0.6.x re-targeted to the first car)");
        Check(TargetSettings.ResolveSelection("", names, 1) == "Outrider(Clone)6749" && TargetSettings.ResolveSelection("", new List<string>(), 0) == "",
            "an empty selection is filled from the cycle position");
        Check(!SettingsPanelManager.DigitsSwitchTabs(false, false) && SettingsPanelManager.DigitsSwitchTabs(false, true) && SettingsPanelManager.DigitsSwitchTabs(true, false),
            "live mode: digits switch tabs only with the mouse over the panel (shift keys stay the game's); Freeze: always");

        // Last-driven targeting resolves the driven car once per pass.
        UnityEngine.Object.Registry.Clear();
        ResetAllCategories();
        VehicleController a = MakeCar(out FakeWheel[] _, 0f);
        VehicleController b = MakeCar(out FakeWheel[] _, 0f);
        a.gameObject.name = "A"; b.gameObject.name = "B";
        UnityEngine.Object.Registry.Add(a);
        UnityEngine.Object.Registry.Add(b);
        var t = new VehicleTuner();
        t.ReapplyNow();
        TargetSettings.Mode = TargetMode.LastDriven;
        b.input.Throttle = 0.8f;
        Time.unscaledTime += 1f;
        SuspensionSettings.Enabled = true;
        SuspensionSettings.SetPresetByName("Race");
        t.ApplyLive();
        Check(t.TargetedCount == 1 && !Near(((FakeWheel)b.powertrain.wheels[0].wheelUAPI).SpringMaxForce, 30000f)
              && Near(((FakeWheel)a.powertrain.wheels[0].wheelUAPI).SpringMaxForce, 30000f), "Last driven (per-pass pick): only the driven car is tuned");
        ResetAllCategories();
        t.ApplyLive();
        TargetSettings.Mode = TargetMode.All;
        b.input.Throttle = 0f;
        UnityEngine.Object.Registry.Clear();
    }
}
