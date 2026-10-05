using System.Collections.Generic;
using ApocalypterSteeringMod.Settings;
using NWH.Common.Vehicles;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using NWH.VehiclePhysics2.Powertrain.Wheel;
using UnityEngine;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// Wheel geometry (0.6.0): camber per wheel through WheelUAPI.Camber, caster/toe per
    /// axle through WheelGroup.CasterAngle/ToeAngle, position by moving the wheel's own
    /// transform. Every value is stock + offset. Allocation-free.
    ///
    /// Hazards handled (gamecode/ line refs in README):
    ///  - CamberController (WheelController3D) rewrites Camber every FixedUpdate and a
    ///    solid axle's WheelGroup.Update() does the same: those wheels are flagged at
    ///    capture and their camber is never written (game components are never disabled).
    ///  - WheelGroup.ApplyGeometryValues only writes caster/toe while its applyCasterAngle /
    ///    applyToeAngle gate is on: a closed gate is opened while a non-zero offset is
    ///    applied and both gates are restored on OFF.
    ///  - Side conventions (camber sign, toe mirror) follow transform.localPosition.x, so
    ///    a moved wheel is never allowed across x = 0 (|x| >= 1 cm, stock side kept).
    ///  - vc.wheelbase and WheelGroup.trackWidth are computed only at NWH init: they go
    ///    stale after a move (Ackermann, solid-axle camber). Not recomputed; the panel warns.
    /// </summary>
    public sealed partial class VehicleTuner
    {
        public const float MinAbsWheelX = 0.01f;   // metres: a moved wheel never crosses the centreline

        private void ApplyAllAlignment()
        {
            AlignmentPreset p = AlignmentSettings.ActivePreset ?? AlignmentPreset.Stock;
            TargetPass(AppliedCat.Alignment, r => AlignmentSettings.Book.ForVehicle(VehicleName(r.Vc)), ApplyAlignment, RestoreAlignment);
        }

        private void RestoreAllAlignment()
        {
            RestorePass(AppliedCat.Alignment, RestoreAlignment);
        }

        /// <summary>
        /// Final local x of a wheel: stock x moved OUTWARD by <paramref name="outwardM"/>,
        /// never closer than MinAbsWheelX to the centreline and never across it. A wheel
        /// that sits on the centreline at stock (a centre wheel) has no side and keeps x.
        /// </summary>
        public static float WheelX(float stockX, float outwardM)
        {
            if (outwardM == 0f || (stockX < MinAbsWheelX && stockX > -MinAbsWheelX))
            {
                return stockX;
            }
            if (stockX > 0f)
            {
                float x = stockX + outwardM;
                return x < MinAbsWheelX ? MinAbsWheelX : x;
            }
            float l = stockX - outwardM;
            return l > -MinAbsWheelX ? -MinAbsWheelX : l;
        }

        private static void ApplyAlignment(VehicleRecord r, AlignmentPreset p)
        {
            bool moved = false;
            foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
            {
                WheelUAPI u = wk.Key;
                if (u == null)
                {
                    continue;
                }
                WheelData d = wk.Value;
                float ox = p.Pos(d.Role, 0) * 0.01f;
                float oy = p.Pos(d.Role, 1) * 0.01f;
                float oz = p.Pos(d.Role, 2) * 0.01f;
                if (ox != 0f || oy != 0f || oz != 0f)
                {
                    moved = true;
                }
                // Never Wheel.localPosition (the struct field is rebuilt at Wheel.Initialize):
                // the WheelController's own transform is honoured live.
                // PosY is INVERTED on purpose: at a fixed resting spring length the
                // body height is ground + springLength - mountLocalY, so lowering the
                // mount (+ PosY) RAISES the car. Positive PosY = taller, as authored.
                u.transform.localPosition = new Vector3(WheelX(d.LocalPos.x, ox), d.LocalPos.y - oy, d.LocalPos.z + oz);

                if (!d.CamberLocked)
                {
                    float desired = d.Camber + p.Camber(d.Role);
                    // The Camber setter clamps to +-16 (WheelController). The excess
                    // rotates the wheel GO about its own forward (local Z on NWH
                    // prefabs), continuing UpdateWheelValues' side sign convention.
                    u.Camber = Mathf.Clamp(desired, -16f, 16f);
                    float overflow = desired - Mathf.Clamp(desired, -16f, 16f);
                    if (overflow > 0.0005f || overflow < -0.0005f)
                    {
                        float side = u.transform.localPosition.x < 0f ? 1f : -1f;
                        Vector3 e = u.transform.localEulerAngles;
                        e.z = d.LocalEuler.z + overflow * side;
                        u.transform.localEulerAngles = e;   // X/Y stay as the group set them (caster/toe)
                    }
                }
            }
            r.AlignmentMoved = moved;

            foreach (KeyValuePair<WheelGroup, GroupData> gk in r.Groups)
            {
                WheelGroup g = gk.Key;
                if (g == null)
                {
                    continue;
                }
                GroupData gd = gk.Value;
                float casterOff = p.Caster(gd.IsFront);
                float toeOff = p.Toe(gd.IsFront);
                bool casterGate = gd.ApplyCaster || casterOff != 0f;
                bool toeGate = gd.ApplyToe || toeOff != 0f;
                bool gateChanged = g.applyCasterAngle != casterGate || g.applyToeAngle != toeGate;
                g.applyCasterAngle = casterGate;
                g.applyToeAngle = toeGate;

                float caster = gd.Caster + casterOff;
                float toe = gd.Toe + toeOff;
                bool valueChanged = false;
                // The setters run ApplyGeometryValues (writes X/Y of each wheel's localEulerAngles,
                // Z preserved, toe mirrored by side). Only write on change: no per-tick transform churn.
                if (g.CasterAngle != caster)
                {
                    g.CasterAngle = caster;
                    valueChanged = true;
                }
                if (g.ToeAngle != toe)
                {
                    g.ToeAngle = toe;
                    valueChanged = true;
                }
                if (gateChanged && !valueChanged)
                {
                    g.ApplyGeometryValues();
                }

                // A gate that was closed at stock and is closed again (offset back to 0):
                // ApplyGeometryValues no longer touches that angle, so put the wheel's own
                // stock angle back (it may still carry our earlier offset).
                if (!casterGate || !toeGate)
                {
                    List<WheelComponent> wheels = g.Wheels;
                    for (int i = 0; i < wheels.Count; i++)
                    {
                        WheelComponent wc = wheels[i];
                        WheelData d;
                        if (wc == null || wc.wheelUAPI == null || !r.Wheels.TryGetValue(wc.wheelUAPI, out d))
                        {
                            continue;
                        }
                        Vector3 e = wc.wheelUAPI.transform.localEulerAngles;
                        float ex = casterGate ? e.x : d.LocalEuler.x;
                        float ey = toeGate ? e.y : d.LocalEuler.y;
                        if (ex != e.x || ey != e.y)
                        {
                            wc.wheelUAPI.transform.localEulerAngles = new Vector3(ex, ey, e.z);
                        }
                    }
                }
            }
        }

        private static void RestoreAlignment(VehicleRecord r)
        {
            // Groups first (their setters rewrite the wheel angles), then every wheel's own
            // captured transform, so the result is exactly the captured state.
            foreach (KeyValuePair<WheelGroup, GroupData> gk in r.Groups)
            {
                WheelGroup g = gk.Key;
                if (g == null)
                {
                    continue;
                }
                GroupData gd = gk.Value;
                g.applyCasterAngle = gd.ApplyCaster;
                g.applyToeAngle = gd.ApplyToe;
                if (g.CasterAngle != gd.Caster)
                {
                    g.CasterAngle = gd.Caster;
                }
                if (g.ToeAngle != gd.Toe)
                {
                    g.ToeAngle = gd.Toe;
                }
            }
            foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
            {
                WheelUAPI u = wk.Key;
                if (u == null)
                {
                    continue;
                }
                WheelData d = wk.Value;
                u.transform.localPosition = d.LocalPos;
                u.transform.localEulerAngles = d.LocalEuler;
                if (!d.CamberLocked)
                {
                    u.Camber = d.Camber;
                }
            }
            r.AlignmentMoved = false;
        }

        private static void RefreshAlignmentBaseline(VehicleRecord r)
        {
            foreach (KeyValuePair<WheelUAPI, WheelData> wk in r.Wheels)
            {
                WheelUAPI u = wk.Key;
                if (u == null)
                {
                    continue;
                }
                wk.Value.Camber = u.Camber;
                wk.Value.LocalPos = u.transform.localPosition;
                wk.Value.LocalEuler = u.transform.localEulerAngles;
            }
            foreach (KeyValuePair<WheelGroup, GroupData> gk in r.Groups)
            {
                WheelGroup g = gk.Key;
                if (g == null)
                {
                    continue;
                }
                gk.Value.Caster = g.CasterAngle;
                gk.Value.Toe = g.ToeAngle;
                gk.Value.ApplyCaster = g.applyCasterAngle;
                gk.Value.ApplyToe = g.applyToeAngle;
            }
        }

        /// <summary>Any tracked vehicle whose wheels are currently moved (wheelbase/track width stale).</summary>
        public bool AnyWheelsMoved
        {
            get
            {
                foreach (KeyValuePair<VehicleController, VehicleRecord> kv in _records)
                {
                    if (kv.Value.AlignmentMoved)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
