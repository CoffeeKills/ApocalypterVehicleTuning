using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using NWH.WheelController3D;
using ApocalypterSteeringMod.Persistence;
using ApocalypterSteeringMod.Runtime;
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

public static class Tests
{
    private static int _fail, _pass;

    private static void Check(bool ok, string what)
    {
        if (ok) { _pass++; Console.WriteLine("  ok   " + what); }
        else { _fail++; Console.WriteLine("  FAIL " + what); }
    }

    private static bool Near(float a, float b, float eps = 1e-3f) { return Math.Abs(a - b) <= eps; }

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
        AerodynamicsModule with = (AerodynamicsModule)vcWith.moduleManager.Components[0];
        Check(Near(with.frontalCd, 0.4f * race.DragScale), "module Cd scaled");
        Check(Near(with.downforcePoints[0].maxForce, 5000f * race.DownforceScale), "downforce point scaled");
        Check(vcWith.moduleManager.Components.Count == 1, "no extra module onboarded");

        // (b) Vehicle WITHOUT a module: onboarded on apply, disabled on restore, reused.
        var vcWithout = MakeCar(out FakeWheel[] _, 0f);
        UnityEngine.Object.Registry.Add(vcWithout);
        tuner.ReapplyNow();
        Check(vcWithout.moduleManager.Components.Count == 1, "module onboarded for vehicle without one");
        AerodynamicsModule added = (AerodynamicsModule)vcWithout.moduleManager.Components[0];
        Check(added.state.isEnabled, "onboarded module enabled");
        Check(Near(added.frontalCd, 0.35f * (race.DragScale - 1f)), "onboarded module Cd = default x the preset's EXCESS drag (0.5.0)");

        AeroSettings.Enabled = false;
        tuner.ApplyLive();
        Check(!added.state.isEnabled, "onboarded module disabled on restore");
        Check(Near(with.frontalCd, 0.4f) && Near(with.downforcePoints[0].maxForce, 5000f), "pre-existing module restored");

        AeroSettings.Enabled = true;
        tuner.ApplyLive();
        Check(vcWithout.moduleManager.Components.Count == 1, "re-enable reuses the onboarded module (no duplicates)");
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
        AerodynamicsModule added = (AerodynamicsModule)vcNone.moduleManager.Components[0];
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
        Check(vcNone.moduleManager.Components.Count == 1, "still exactly one onboarded module");
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
        Check(vc.moduleManager.Components.Count == 0, "Street (drag x1.0) onboards nothing on an aero-less vehicle (0.4.0: full 0.35 Cd)");

        // Less drag than stock cannot ADD drag.
        AeroPreset custom = AeroSettings.BeginEdit();
        custom.DragScale = 0.8f;
        custom.DownforceScale = 1.5f;
        tuner.ApplyLive();
        Check(vc.moduleManager.Components.Count == 0, "drag x0.8 (+ downforce x1.5) adds no module/drag (0.4.0 added 0.28 Cd)");

        custom.DragScale = 1.2f;
        tuner.ApplyLive();
        AerodynamicsModule m = vc.moduleManager.Components.Count == 1 ? (AerodynamicsModule)vc.moduleManager.Components[0] : null;
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
        Check(!threw && dead.moduleManager.Components.Count == 0,
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
        UnityEngine.Object.Registry.Clear();
        var t4 = new VehicleTuner();
        UnityEngine.Object.Registry.Add(idle);
        t4.ReapplyNow();
        Check(t4.TryGetTelemetry(out ts) && Near(ts.SpeedKmh, 10.8f), "no input anywhere: fastest car wins");
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

        // 5 -> 6 gears.
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
        p.TransmissionMode = GearboxMode.Manual;
        tuner.ApplyLive();
        Check(t.transmissionType == TransmissionComponent.TransmissionShiftType.Manual, "transmission mode Manual applied");
        GearboxSettings.Enabled = false;
        tuner.ApplyLive();
        Check(Near(cl.slipTorque, 500f) && Near(cl.engagementRange, 400f) && Near(cl.engagementRPM, 1200f)
              && t.transmissionType == TransmissionComponent.TransmissionShiftType.Automatic, "OFF restores clutch and transmission type");

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
            default: return GearboxPreset.Custom;
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
        Check(allRoundTrip, "Serialize -> Import -> Serialize is byte-identical for all nine books");
        Check(PresetCodec.KeyCount(PresetCategory.Alignment) == 20 && PresetCodec.KeyCount(PresetCategory.Gearbox) == 17,
            "alignment carries 4 camber + caster/toe + 12 position keys; gearbox 12 gear + 5 keys");

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
        Check(UiSettings.TelemetryEnabled && Near(UiSettings.TelemetryScale, 1f) && UiSettings.TelemetryPosition == TelemetryCorner.BottomLeft,
            "telemetry defaults: on, x1, bottom left");
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
        Check(!UiSettings.FreezeWhileOpen && UiSettings.TelemetryEnabled, "0.5.0 cfg: new behaviour defaults (live panel, telemetry on)");

        // Hand-edited out-of-range values clamp on load; name-only enum parsing.
        string bad = Path.Combine(dir, "bad060.cfg");
        File.WriteAllText(bad,
            "[Alignment.Custom]\nCamberFL = 99\nPosXRR = -400\n\n[Gearbox.Custom]\nGearCount = 40\nGear3Scale = 0.1\nTransmissionMode = 7\n\n" +
            "[UI]\nPanelWidth = 5000\nPanelScale = 0\nLastTab = 42\n\n[Telemetry]\nPosition = Sideways\n");
        ModConfig.Load(new ConfigFile(bad, true));
        Check(Near(AlignmentPreset.Custom.CamberFL, 45f) && Near(AlignmentPreset.Custom.PosXRR, -100f), "alignment values clamped to +-45 deg / +-100 cm");
        Check(GearboxPreset.Custom.GearCount == 12 && Near(GearboxPreset.Custom.Scale(3), 0.25f), "gear count clamped to 12, gear factor to 0.25");
        Check(GearboxPreset.Custom.TransmissionMode == GearboxMode.Stock && UiSettings.TelemetryPosition == TelemetryCorner.BottomLeft,
            "numeric / unknown enum names fall back (Stock, BottomLeft)");
        Check(Near(UiSettings.PanelWidth, 1000f) && Near(UiSettings.PanelScale, 0.3f) && UiSettings.LastTab == 9, "panel width/scale/last tab clamped");
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
        UiSettings.LastTab = 8;
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
        Check(UiSettings.FreezeWhileOpen && Near(UiSettings.PanelWidth, 620f) && UiSettings.LastTab == 8
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
        Check(!wide.Stacked && Near(wide.RowHeight, 58f) && Near(wide.Slider.X, 328f) && Near(wide.Slider.W, 748f - 328f - 236f)
              && Near(wide.Value.X, 748f - 82f - 150f) && Near(wide.Reset.X, 748f - 78f), "wide rows reproduce 0.5.0's geometry exactly");
        Check(PanelLayout.SliderRow(PanelLayout.ContentWidth(460f), true).Stacked, "the default 460 px width stacks the rows");

        Check(Near(PanelLayout.EffectiveWidth(800f, 1920f, 1080f, 1f), 800f), "800 px fits a 1080p screen at x1");
        Check(Near(PanelLayout.EffectiveWidth(800f, 1440f, 1080f, 2f), 700f), "x2 on a 4:3 screen: width capped to the canvas (720 - 20)");
        Check(Near(PanelLayout.ScaleFactor(1080f, 1f), 1f) && Near(PanelLayout.ScaleFactor(2160f, 1f), 2f) && Near(PanelLayout.ScaleFactor(1080f, 0.5f), 0.5f),
            "scale factor = Screen.height / 1080 x PanelScale (x1 renders like 0.5.0)");

        Check(PanelLayout.TabForDigit(1) == 0 && PanelLayout.TabForDigit(9) == 8 && PanelLayout.TabForDigit(0) == 9 && PanelLayout.TabForDigit(11) == -1,
            "digit hotkeys: 1..9 -> tabs 1..9, 0 -> tab 10");
        Check(SettingsPanel.TabCount == 10 && SettingsPanel.TabNames.Length == 10 && SettingsPanel.TabNames[9] == "Panel", "ten tabs, Panel last");

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
}
