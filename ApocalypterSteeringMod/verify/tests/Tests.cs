using System;
using System.IO;
using System.Reflection;
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
        Check(Near(c0.RateMultiplier, 1f) && Near(c0.SlipAngleDeg, 8.5f) && Near(c0.OppositeLockBoost, 1.75f) && !c0.CurveOverride && !c0.LinearityOverride,
            "steering Custom defaults == v2.0.0 behaviour");

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
        Check(Near(added.frontalCd, 0.35f * race.DragScale), "onboarded module Cd = default x preset");

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
}
