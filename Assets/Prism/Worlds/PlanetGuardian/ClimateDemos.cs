using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Demos;
using UnityEngine;

namespace Prism.Worlds.PlanetGuardian
{
    /// <summary>
    /// Pocket demonstrations for Planet Guardian's four supporting concepts — the small,
    /// manipulable phenomenon that blooms above a concept in the atrium, before the learner ever
    /// enters the world proper. Registered by <see cref="ConceptDemoForAttribute"/>, discovered by
    /// <c>ConceptDemoRegistry</c> without editing that shared file.
    ///
    /// All four share one piece of real physics — Stefan-Boltzmann radiative balance or a real
    /// gradient/fold dynamical system — at a scale honest enough to solve by hand and rich enough
    /// to show the real behaviour. None of them re-teach Planet Guardian itself; each isolates one
    /// atomic idea the world later combines. See NOTES.md for what is honest vs approximate in
    /// each, and for the numerical verification each one was checked against before being written
    /// here (no compiler in this loop, so the arithmetic was run externally first).
    /// </summary>

    // -----------------------------------------------------------------------------------------
    // ALBEDO — one beam, and how much of it comes back.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A swatch of surface under a fixed light. The free hand sets how reflective it is; the
    /// swatch's own colour IS that number (brighter = more reflective, which is not a metaphor,
    /// it is literally what albedo means), and a second, dimmer beam peels away carrying exactly
    /// that fraction of the light back out. What is not directly grabbed but genuinely computed:
    /// the swatch also has a real temperature, integrated from the fraction that was NOT
    /// reflected — C dT/dt = (1-albedo)*flux - sigma*T^4, the same equation Planet Guardian runs
    /// at planetary scale, run here for one small absorbing body. Turning the swatch pale visibly
    /// cools it; turning it dark visibly warms it — the consequence, not just the reflection.
    /// </summary>
    [ConceptDemoFor("albedo")]
    public class AlbedoDemo : ConceptDemo
    {
        Transform _sun;
        Transform _swatch;
        Material _swatchMat;
        CurveView _inBeam, _reflectBeam;

        float _albedo = 0.35f;
        float _temperature = 250f;             // K

        const float Sigma = 5.670374419e-8f;   // real Stefan-Boltzmann constant
        const float IncidentFlux = 340f;       // W/m^2 — a real, Earth-orbit-plausible number
        const float HeatCapacity = 12f;        // demo-scale inertia: seconds, not years

        static readonly Vector3 SunPos = new Vector3(-0.78f, 0.55f, 0f);
        static readonly Vector3 ReflectDir = new Vector3(0.72f, 0.38f, 0f);

        public override void Build()
        {
            _sun = Ball(0.075f, PrismPalette.Gold, 0.9f, "sun");
            _sun.localPosition = SunPos;

            _swatchMat = PrismMaterials.CeramicBody(Color.Lerp(PrismPalette.Violet, PrismPalette.Warm, _albedo), 0.35f);
            _swatch = Body(PrismMesh.Icosphere(2), 0.30f, _swatchMat, "swatch");

            _inBeam = Curve(PrismPalette.Gold, 0.010f, 3f);
            _reflectBeam = Curve(PrismPalette.Cyan, 0.009f, 2f);
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand))
                _albedo = Mathf.Clamp01(Mathf.InverseLerp(-0.65f, 0.65f, hand.y));

            // real single-body radiative balance, semi-implicit for the same reason Planet
            // Guardian's own stepper is: unconditionally stable regardless of how fast the hand
            // moves the albedo.
            float absorbed = (1f - _albedo) * IncidentFlux;
            float t3 = _temperature * _temperature * _temperature;
            float outgoing = Sigma * t3 * _temperature;
            float dOutdT = 4f * Sigma * t3;
            float denom = HeatCapacity / dt + dOutdT;
            _temperature = Mathf.Clamp(_temperature + (absorbed - outgoing) / Mathf.Max(denom, 1e-6f), 80f, 400f);

            // The swatch's colour IS the albedo — dark absorbs, pale reflects — and its
            // luminance rises with its own computed temperature, a real consequence rendered,
            // not a second control.
            _swatchMat.SetColor("_Tint", Color.Lerp(PrismPalette.Violet, PrismPalette.Warm, _albedo));
            _swatchMat.SetFloat("_Luminance", 0.12f + Mathf.InverseLerp(150f, 320f, _temperature) * 0.55f);

            Segment(_inBeam, _sun.localPosition, _swatch.localPosition);
            _inBeam.Mat.SetFloat("_Strength", 0.85f);

            Segment(_reflectBeam, _swatch.localPosition, _swatch.localPosition + ReflectDir);
            _reflectBeam.Mat.SetFloat("_Strength", 0.12f + _albedo * 0.55f);
            _reflectBeam.Mat.SetFloat("_Packets", Mathf.Lerp(1f, 7f, _albedo));
        }
    }

    // -----------------------------------------------------------------------------------------
    // ENERGY BALANCE — two flows, and whether they match.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The atomic mechanism inside Planet Guardian, isolated: one body, one incoming flow, one
    /// outgoing flow, one control. The free hand sets how much arrives; how much leaves is
    /// computed, honestly, from the body's own temperature via Stefan-Boltzmann — nothing here
    /// is a blackbody with albedo or greenhouse trim, on purpose, so the ONE relationship (in
    /// versus out) is legible without anything else competing for attention. Colour follows
    /// Planet Guardian's own law exactly — the body's hue is the current imbalance, cyan losing,
    /// the ramp's warm end gaining — so a learner who meets this first recognises the language
    /// when they later meet the full planet.
    /// </summary>
    [ConceptDemoFor("energy-balance")]
    public class EnergyBalanceDemo : ConceptDemo
    {
        Transform _sun, _body;
        Material _bodyMat;
        CurveView _inFlow, _outFlow;

        float _temperature = 255f;             // K — Earth's real airless blackbody temperature
        float _flux = 240f;                    // W/m^2 — Earth's real absorbed solar flux

        const float Sigma = 5.670374419e-8f;
        const float HeatCapacity = 15f;
        const float FluxMin = 60f, FluxMax = 480f;
        const float ImbalanceScale = 80f;

        static readonly Vector3 OutDir = new Vector3(0f, -0.72f, 0f);

        public override void Build()
        {
            _sun = Ball(0.065f, PrismPalette.Gold, 0.9f, "sun");
            _sun.localPosition = new Vector3(0f, 0.78f, 0f);

            _bodyMat = PrismMaterials.CeramicBody(PrismPalette.Lavender, 0.35f);
            _body = Body(PrismMesh.Icosphere(2), 0.27f, _bodyMat, "body");

            _inFlow = Curve(PrismPalette.Gold, 0.010f, 3f);
            _outFlow = Curve(PrismPalette.Cyan, 0.010f, 3f);
        }

        protected override void OnTick(float dt)
        {
            if (TryFreeLocal(out var hand))
                _flux = Mathf.Lerp(FluxMin, FluxMax, Mathf.InverseLerp(-0.65f, 0.65f, hand.y));

            float t3 = _temperature * _temperature * _temperature;
            float outgoing = Sigma * t3 * _temperature;
            float dOutdT = 4f * Sigma * t3;
            float denom = HeatCapacity / dt + dOutdT;
            float imbalance = _flux - outgoing;
            _temperature = Mathf.Clamp(_temperature + imbalance / Mathf.Max(denom, 1e-6f), 80f, 400f);

            Segment(_inFlow, _sun.localPosition, _body.localPosition);
            float inRate = Mathf.InverseLerp(FluxMin, FluxMax, _flux);
            _inFlow.Mat.SetFloat("_Speed", Mathf.Lerp(0.3f, 2.2f, inRate));
            _inFlow.Mat.SetFloat("_Packets", Mathf.Lerp(1f, 8f, inRate));

            Segment(_outFlow, _body.localPosition, _body.localPosition + OutDir);
            float outRate = Mathf.InverseLerp(FluxMin, FluxMax, outgoing);
            _outFlow.Mat.SetFloat("_Speed", Mathf.Lerp(0.3f, 2.2f, outRate));
            _outFlow.Mat.SetFloat("_Packets", Mathf.Lerp(1f, 8f, outRate));

            // Planet Guardian's own colour law, unchanged: hue is imbalance, not temperature.
            float u = 0.5f + 0.5f * Mathf.Clamp(imbalance / ImbalanceScale, -1f, 1f);
            _bodyMat.SetColor("_Tint", PrismPalette.Spectral(u));
        }
    }

    // -----------------------------------------------------------------------------------------
    // EQUILIBRIUM — a real landscape, and the difference between a valley and a hilltop.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A ball on a real potential-energy landscape, U(x) = x^4 - 2x^2 — two valleys with a
    /// hilltop between them, the same double-well shape Planet Guardian's own climate sits on
    /// (its two branches, its unstable point in between), shown here in the abstract with
    /// nothing about climate in it at all. The free hand picks the ball up and puts it down
    /// anywhere on the terrain; on release, real damped dynamics (F = -dU/dx) take over. Drop it
    /// on a slope and it settles into the nearest valley. Drop it exactly on the hilltop and nothing
    /// happens — until it does, because no hand is that exact, which is the honest lesson about
    /// what "unstable" actually means.
    /// </summary>
    [ConceptDemoFor("equilibrium")]
    public class EquilibriumDemo : ConceptDemo
    {
        Mesh _terrainMesh;
        Material _terrainMat;
        Transform _ball;
        Material _ballMat;

        float _x = 1f;
        float _v;
        bool _held;

        const int Res = 26;
        const float Extent = 1.3f;
        const float HeightScale = 0.40f;
        const float Damping = 2.2f;
        const float BallRadius = 0.075f;

        static float U(float x) => x * x * x * x - 2f * x * x;
        static float Force(float x) => 4f * x - 4f * x * x * x;   // -dU/dx

        public override void Build()
        {
            _terrainMat = FlatMaterial(PrismPalette.Lavender, 0.26f);
            _terrainMesh = Sheet(Res, _terrainMat, out _, Extent);

            // The terrain never moves again after this — shape it once into the double well.
            var verts = new List<Vector3>();
            _terrainMesh.GetVertices(verts);
            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                v.y = U(v.x) * HeightScale;
                verts[i] = v;
            }
            SheetApply(_terrainMesh, verts);

            _ballMat = PrismMaterials.CeramicBody(Tint, 0.5f);
            _ball = Body(PrismMesh.Icosphere(2), BallRadius, _ballMat, "ball");
        }

        protected override void OnTick(float dt)
        {
            bool pinching = TryFreeLocal(out var hand) && FreePinch > 0.55f;

            if (!_held && pinching && (hand - _ball.localPosition).sqrMagnitude < 0.32f * 0.32f)
                _held = true;
            if (_held && !pinching)
                _held = false;

            if (_held)
            {
                _x = Mathf.Clamp(hand.x, -Extent, Extent);
                _v = 0f;
            }
            else
            {
                const int substeps = 6;
                float sub = dt / substeps;
                for (int s = 0; s < substeps; s++)
                {
                    _v += (Force(_x) - Damping * _v) * sub;
                    _x += _v * sub;
                }
                _x = Mathf.Clamp(_x, -Extent, Extent);
            }

            _ball.localPosition = new Vector3(_x, U(_x) * HeightScale + BallRadius * 1.05f, 0f);

            // A quiet glow when it has genuinely come to rest — not a score, just a "yes, here".
            bool settled = !_held && Mathf.Abs(_v) < 0.03f;
            _ballMat.SetFloat("_Luminance", settled ? 0.8f : 0.45f);
        }
    }

    // -----------------------------------------------------------------------------------------
    // TIPPING POINTS — the same landscape, except now the learner tilts it, and cannot untilt it back.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The close cousin of the constellation's existing FeedbackDemo, and deliberately a
    /// different mechanism rather than a restatement of it: FeedbackDemo shows a SINGLE fixed
    /// point that is either stable or unstable depending on a gain. This shows something
    /// FeedbackDemo cannot — a landscape with TWO stable valleys where one of them can vanish.
    ///
    /// Real model: dx/dt = c + x - x^3, the standard cusp/fold system used across climate,
    /// ecology and engineering wherever "tipping point" is used technically rather than loosely.
    /// For |c| less than 2/(3*sqrt(3)) ~= 0.385 there are two valleys and a hilltop between them;
    /// past that, on either side, one valley is mathematically gone. The free hand does not touch
    /// the ball — it drags a control bead that tilts the whole landscape, and the ball is simply
    /// wherever the (real, computed) landscape leaves it.
    ///
    /// Push the control past the edge: the current valley disappears from under the ball, and it
    /// rolls the rest of the way to the only valley left. Push the control back to exactly where
    /// it was when that happened: the ball does NOT come back, because the valley it is now in
    /// does not vanish until a DIFFERENT threshold, on the far side. Undoing the push is not the
    /// same as undoing the outcome. That asymmetry is the whole concept, and it was verified
    /// against the exact equations before being written here — pushing c from 0 to 0.6 and back
    /// to 0 leaves the ball on the opposite side from where it started, every time.
    /// </summary>
    [ConceptDemoFor("tipping-points")]
    public class TippingPointsDemo : ConceptDemo
    {
        Mesh _terrainMesh;
        Material _terrainMat;
        readonly List<Vector3> _terrainVerts = new List<Vector3>();

        Transform _ball;
        Material _ballMat;
        Transform _controlBead;
        Material _controlMat;
        CurveView _rail;

        float _x = -1f;      // the ball's real state
        float _c;            // the tilt the learner controls
        bool _wasJumping;

        const int Res = 22;
        const float Extent = 1.35f;
        const float HeightScale = 0.40f;
        const float BallRadius = 0.075f;
        const float CMax = 0.6f;                 // comfortably past the +-0.385 fold points

        static readonly Vector3 RailBase = new Vector3(0.88f, -0.7f, 0f);
        static readonly Vector3 RailTop = new Vector3(0.88f, 0.7f, 0f);

        static float U(float x, float c) => x * x * x * x * 0.25f - x * x * 0.5f - c * x;
        static float Force(float x, float c) => c + x - x * x * x;   // -dU/dx

        public override void Build()
        {
            _terrainMat = FlatMaterial(Tint, 0.30f);
            _terrainMesh = Sheet(Res, _terrainMat, out _, Extent);
            _terrainMesh.GetVertices(_terrainVerts);   // captured once; only .y changes hereafter

            _ballMat = PrismMaterials.CeramicBody(PrismPalette.Warm, 0.55f);
            _ball = Body(PrismMesh.Icosphere(2), BallRadius, _ballMat, "ball");

            _rail = Curve(PrismPalette.Warm, 0.008f);
            Segment(_rail, RailBase, RailTop);   // static; the rail itself never moves

            _controlMat = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.6f);
            _controlBead = Body(PrismMesh.Icosphere(1), 0.05f, _controlMat, "controlBead");
        }

        protected override void OnTick(float dt)
        {
            // The free hand drives the TILT, never the ball directly — the ball's fate is a
            // consequence of the landscape, not something puppeted by hand, which is the point.
            if (TryFreeLocal(out var hand) && FreePinch > 0.5f
                && (hand - _controlBead.localPosition).sqrMagnitude < 0.4f * 0.4f)
            {
                float t = Mathf.InverseLerp(RailBase.y, RailTop.y, hand.y);
                _c = Mathf.Lerp(-CMax, CMax, Mathf.Clamp01(t));
            }
            _controlBead.localPosition = Vector3.Lerp(RailBase, RailTop,
                                                       Mathf.InverseLerp(-CMax, CMax, _c));

            float prevX = _x;
            const int substeps = 6;
            float sub = dt / substeps;
            for (int s = 0; s < substeps; s++)
                _x += Force(_x, _c) * sub;
            _x = Mathf.Clamp(_x, -Extent, Extent);

            // Threshold checked against the actual dynamics (see NOTES.md): peak per-frame speed
            // during a genuine jump is ~0.98 units/s at these constants, while the slow approach
            // to a vanishing valley — real critical slowing down near the fold — stays well under
            // 0.5. 0.5 catches the dramatic part without firing on ordinary drift.
            float speed = Mathf.Abs(_x - prevX) / Mathf.Max(dt, 1e-4f);
            bool jumping = speed > 0.5f;
            if (jumping && !_wasJumping) Voice?.Tension(transform.position, 0.3f);
            if (!jumping && _wasJumping) Voice?.Settle(transform.position, 0.4f);
            _wasJumping = jumping;

            _ball.localPosition = new Vector3(_x, U(_x, _c) * HeightScale + BallRadius * 1.05f, 0f);
            _ballMat.SetColor("_Tint", Color.Lerp(PrismPalette.Warm, PrismPalette.Coral, Mathf.Clamp01(speed / 1.2f)));

            // Reshape the landscape for the current tilt — the one mesh here that must be rebuilt
            // every frame, because the landscape changing shape under the ball IS the demonstration.
            for (int i = 0; i < _terrainVerts.Count; i++)
            {
                var v = _terrainVerts[i];
                v.y = U(v.x, _c) * HeightScale;
                _terrainVerts[i] = v;
            }
            SheetApply(_terrainMesh, _terrainVerts);
        }
    }
}
