using System;
using System.Collections.Generic;
using UnityEngine;

// Positions are in the same 2.9 m car coordinate system for every visual LOD.
internal sealed class TrackBodyDamage
{
    internal readonly struct Mark
    {
        internal readonly Vector3 Center, Outward, Along;
        internal readonly float Width, Height, Scuff, Depth;
        internal readonly bool TireRub;
        private readonly float sin, cos;

        internal Mark(Vector3 center, Vector3 outward, float width, float height, float scuff, float depth,
            bool tireRub = false, float rotation = 0f)
        {
            Center = center;
            Outward = outward;
            Along = Vector3.Cross(outward, Vector3.up);
            Width = width;
            Height = height;
            Scuff = scuff;
            Depth = depth;
            TireRub = tireRub;
            sin = Mathf.Sin(rotation);
            cos = Mathf.Cos(rotation);
        }

        internal Vector2 Project(Vector3 point)
        {
            var delta = point - Center;
            float u = Vector3.Dot(delta, Along) / Width;
            float v = delta.y / Height;
            return new Vector2(u * cos - v * sin, u * sin + v * cos);
        }

        internal bool OnPanelSide(Vector3 point) => Vector3.Dot(point, Outward) >
            (TireRub ? Mathf.Abs(Center.x) * .64f : Outward.x == 0f ? 1.06f : .39f);

        internal float SurfaceWeight(Vector3 point)
        {
            float distance = Vector3.Dot(point, Outward);
            float coverage = Outward.x == 0f ? Mathf.InverseLerp(1.06f, 1.25f, distance) :
                Mathf.InverseLerp(.39f, .52f, distance);
            return coverage * coverage * (3f - 2f * coverage);
        }
    }

    // Front, rear, then front fender/door/rear quarter on each side.
    internal readonly Mark[] Marks = new Mark[8];
    private const int MaximumTireMarks = 12;
    private readonly List<Mark> tireMarks = new(MaximumTireMarks);
    internal IReadOnlyList<Mark> TireMarks => tireMarks;
    internal int Revision { get; private set; }
    private bool HasPanelDamage { get; set; }
    internal bool HasDent { get; private set; }
    internal bool HasMarks => HasPanelDamage || tireMarks.Count != 0;

    internal void Clear()
    {
        if (!HasMarks) return;
        Array.Clear(Marks, 0, Marks.Length);
        tireMarks.Clear();
        HasPanelDamage = false;
        HasDent = false;
        Revision++;
    }

    internal Vector3 Add(Vector2 otherOffset, bool side, float damage, bool modified = false, Vector2? panelContact = null)
    {
        float scuff = Mathf.Lerp(.39f, .95f, Mathf.InverseLerp(.5f, 22f, damage));
        float depth = damage >= 6f ? Mathf.Lerp(.030f, .13f, Mathf.InverseLerp(6f, 32f, damage)) : 0f;
        int slot;
        Vector3 center, normal;
        float width, height;
        if (side)
        {
            depth = Mathf.Min(depth, .10f);
            float sign = (panelContact?.x ?? otherOffset.x) < 0f ? -1f : 1f;
            float z = Mathf.Clamp(panelContact?.y ?? otherOffset.y * .52f, -1.10f, 1.00f);
            int section = z > .48f ? 0 : z < -.43f ? 2 : 1;
            slot = 2 + (sign < 0f ? 0 : 3) + section;
            // The lower door also has support vertices on the lowest-detail short-track body.
            center = modified ? new Vector3(.64f * sign, .23f, z) :
                new Vector3(.57f * sign, depth > 0f ? .27f : .33f, z);
            normal = Vector3.right * sign;
            width = Mathf.Lerp(.28f, .48f, Mathf.InverseLerp(2f, 26f, damage));
            height = modified ? .13f : .17f;
        }
        else
        {
            float sign = (panelContact?.y ?? otherOffset.y) < 0f ? -1f : 1f;
            slot = sign > 0f ? 0 : 1;
            float bumperHeight = sign > 0f ? .27f : modified ? .23f : .34f;
            center = new Vector3(Mathf.Clamp(panelContact?.x ?? otherOffset.x * .5f, -.65f, .65f), bumperHeight, 1.35f * sign);
            normal = Vector3.forward * sign;
            scuff = Mathf.Max(.48f, scuff);
            depth = Mathf.Min(depth, .065f);
            width = Mathf.Lerp(.28f, .50f, Mathf.InverseLerp(1f, 29f, damage));
            // The Modified's rear bumper has two outboard sections with an open center.
            if (modified && sign < 0f)
                width = Mathf.Max(width, .54f - Mathf.Abs(center.x));
            height = modified ? .12f : .20f;
        }

        // Keep the deepest impression on each panel. A lighter rub cannot erase an existing dent.
        Mark old = Marks[slot];
        if (depth > old.Depth || depth == old.Depth && scuff > old.Scuff)
        {
            Marks[slot] = new Mark(center, normal, width, height, scuff, depth);
            HasPanelDamage = true;
            HasDent |= depth > 0f;
            Revision++;
        }
        return center;
    }

    internal void AddTireRub(Vector3 center, float side, float radius, float rotation)
    {
        foreach (var mark in tireMarks)
        {
            if (mark.Outward.x == side && (mark.Center - center).sqrMagnitude < radius * radius * .18f)
                return;
        }
        if (tireMarks.Count == MaximumTireMarks) tireMarks.RemoveAt(0);
        tireMarks.Add(new Mark(center, Vector3.right * side, radius, radius, .94f, 0f, tireRub: true, rotation: rotation));
        Revision++;
    }
}

public sealed partial class TrackPreview
{
    private const string PrototypeBodyResource = "ChevroletCupCamaroShortTrack";
    private readonly TrackBodyDamage[] carBodyDamage = new TrackBodyDamage[CarCount];
    private readonly bool[] bodyDamageDirty = new bool[CarCount];
    private Material bodyScuffMaterial;
    private ParticleSystem bodyContactSmoke, bodyContactSparks;

    private static bool IsBodyDamagePrototype(int index, string resource) =>
        index == 0 && resource != null && resource.StartsWith(PrototypeBodyResource, StringComparison.Ordinal);

    private static bool HasModifiedNerfBars(string resource) =>
        resource != null && resource.StartsWith("TourModified", StringComparison.Ordinal);

    internal bool CanPreviewTireRub => SessionRun != null && carVisualLods[0]?.Current != null && trafficCars?[0]?.InGarage != true &&
        cars[0].gameObject.activeInHierarchy;
    internal bool CanPreviewWallScrape => CanPreviewTireRub && IsBodyDamagePrototype(0, carVisualLods[0].Current.Resource);
    internal bool CanPreviewBodyDamage => CanPreviewWallScrape ||
        CanPreviewTireRub && HasModifiedNerfBars(carVisualLods[0].Current.Resource);

    private void ResetTrackBodyDamage()
    {
        for (int i = 0; i < carBodyDamage.Length; i++)
        {
            if (carBodyDamage[i] == null) continue;
            carBodyDamage[i].Clear();
            ApplyTrackBodyDamage(i, carVisualLods[i]?.Current);
        }
        bodyContactSmoke?.Clear();
        bodyContactSparks?.Clear();
    }

    private void ApplyTrackBodyDamage(int index, CarVisual visual)
    {
        bodyDamageDirty[index] = false;
        var damage = carBodyDamage[index];
        if (visual == null || damage == null || !damage.HasMarks && visual.DamageSurface == null)
            return;

        if (visual.DamageSurface == null)
        {
            if (bodyScuffMaterial == null)
            {
                var shader = Resources.Load<Shader>("Startup/TrackBodyScuff");
                if (shader != null && shader.isSupported)
                {
                    bodyScuffMaterial = new Material(shader);
                    materials.Add(bodyScuffMaterial);
                }
            }
            visual.DamageSurface = visual.Body.gameObject.AddComponent<TrackBodyDamageSurface>();
            var visuals = carVisualLods[index];
            visual.DamageSurface.Bind(bodyScuffMaterial, IsBodyDamagePrototype(index, visual.Resource),
                HasModifiedNerfBars(visual.Resource), visuals.BodyPaint, visuals.HoodPaint);
        }
        visual.DamageSurface.Apply(damage);
    }

    private void PresentTrackBodyDamage()
    {
        for (int i = 0; i < bodyDamageDirty.Length; i++)
            if (bodyDamageDirty[i]) ApplyTrackBodyDamage(i, carVisualLods[i]?.Current);
    }

    private void OnVehicleBodyImpact(VehicleCollision001.BodyImpact impact)
    {
        Vector3 point = RecordTrackPanelImpact(impact.First, impact.FirstOffset, impact.FirstSide, impact.Damage, impact.FirstPanelContact);
        if (impact.Second >= 0)
            RecordTrackPanelImpact(impact.Second, impact.SecondOffset, impact.SecondSide, impact.Damage, impact.SecondPanelContact);
        if (!impact.SideToSide) return;

        float speed = SessionRun.CurrentSpeed(impact.First);
        if (impact.Second >= 0)
        {
            speed = Mathf.Max(speed, SessionRun.CurrentSpeed(impact.Second));
            RecordTireRubFrom(impact.Second, impact.First, impact.SecondOffset, impact.FirstOffset);
            RecordTireRubFrom(impact.First, impact.Second, impact.FirstOffset, impact.SecondOffset);
        }
        EmitBodyContactEffects(cars[impact.First], point, impact.Damage, speed);
    }

    private Vector3 RecordTrackPanelImpact(int index, Vector2 offset, bool side, float damage, Vector2? panelContact = null)
    {
        var visual = carVisualLods[index]?.Current;
        float sideContactStation = panelContact?.y ?? offset.y * .52f;
        bool modified = HasModifiedNerfBars(visual?.Resource);
        bool nerfBar = modified && sideContactStation >= -.50f && sideContactStation <= .76f;
        if (visual == null || side && !nerfBar && !IsBodyDamagePrototype(index, visual.Resource))
        {
            float halfWidth = raceCarHalfWidths[index] > 0f ? raceCarHalfWidths[index] : .58f;
            return new Vector3(Mathf.Sign(panelContact?.x ?? offset.x) * halfWidth, .33f, Mathf.Clamp(sideContactStation, -1.2f, 1.2f));
        }
        var state = carBodyDamage[index] ??= new TrackBodyDamage();
        Vector3 contact = state.Add(offset, side, damage, modified, panelContact);
        bodyDamageDirty[index] = true;
        // The mesh work is deferred to presentation so collision and scoring do not wait on it.
        return cars[index].InverseTransformPoint(visual.Body.TransformPoint(contact));
    }

    private void RecordTireRubFrom(int source, int target, Vector2 sourceToTarget, Vector2 targetToSource)
    {
        var wheels = raceWheels[source];
        if (wheels == null || SessionRun.CurrentSpeed(source) < 1.2f || carVisualLods[target]?.Current == null) return;

        float sourceSide = Mathf.Sign(sourceToTarget.x);
        float targetSide = Mathf.Sign(targetToSource.x);
        float halfWidth = raceCarHalfWidths[target] > 0f ? raceCarHalfWidths[target] : .58f;
        Quaternion rotation = Quaternion.Inverse(cars[target].rotation) * cars[source].rotation;
        Vector3 offset = new Vector3(targetToSource.x, 0f, targetToSource.y);
        for (int axle = 0; axle < 2; axle++)
        {
            bool front = axle == 0;
            if (front && SessionRun.EmergencyBrakeLockup(source)) continue;
            if (!wheels.TryGetTireSidewall(front, sourceSide, out Vector3 sidewall, out float radius, out float spin))
                continue;
            Vector3 contact = offset + rotation * sidewall;
            if (!TireSidewallReachesPanel(contact, targetSide, halfWidth, radius)) continue;
            RecordTireRub(target, contact, targetSide, radius, spin);
        }
    }

    internal static bool TireSidewallReachesPanel(Vector3 contact, float side, float halfWidth, float radius)
    {
        float nearSide = contact.x * side;
        return nearSide >= halfWidth - .22f && nearSide <= halfWidth + .08f &&
            Mathf.Abs(contact.z) < VehicleCollision001.ContactLength * .5f + radius * .45f;
    }

    private void RecordTireRub(int target, Vector3 contact, float side, float radius, float spin)
    {
        var visual = carVisualLods[target].Current;
        Vector3 localPoint = visual.Body.InverseTransformPoint(cars[target].TransformPoint(contact));
        var state = carBodyDamage[target] ??= new TrackBodyDamage();
        state.AddTireRub(localPoint, side, Mathf.Clamp(radius, .10f, .26f), spin);
        bodyDamageDirty[target] = true;
    }

    private void OnWallBodyImpact(int index, Vector2 offset, bool side, float damage, float speed)
    {
        Vector3 point = RecordTrackPanelImpact(index, offset, side, damage);
        if (side)
            EmitBodyContactEffects(cars[index], point, damage, speed);
    }

    internal void PreviewWallScrape()
    {
        if (CanPreviewWallScrape)
            OnWallBodyImpact(0, new Vector2(1f, -1.65f), side: true, damage: 2.5f, speed: 24f);
    }

    internal void PreviewSideImpact()
    {
        if (!CanPreviewBodyDamage) return;
        OnVehicleBodyImpact(new VehicleCollision001.BodyImpact(0, -1, new Vector2(-1f, .12f), default, true, true, 18f));
        PreviewTireDoughnut();
    }

    internal void PreviewTireDoughnut()
    {
        if (!CanPreviewTireRub) return;
        // Place the simulated rival's sidewall on the player car beside the driver.
        float halfWidth = raceCarHalfWidths[0] > 0f ? raceCarHalfWidths[0] : .58f;
        RecordTireRub(0, new Vector3(-halfWidth, .23f, .06f), -1f, .22f, .7f);
    }

    private void AdvanceBodyContactEffects(float seconds)
    {
        bodyContactSmoke?.Simulate(seconds, false, false, false);
        bodyContactSparks?.Simulate(seconds, false, false, false);
    }

    private void EmitBodyContactEffects(Transform car, Vector3 localPoint, float damage, float speed)
    {
        if (raceCamera == null || !raceCamera.enabled || !car.gameObject.activeInHierarchy)
            return;

        Vector3 origin = car.TransformPoint(localPoint);
        float strength = Mathf.InverseLerp(.5f, 22f, damage);
        float side = localPoint.x < 0f ? -1f : 1f;
        int puffs = Mathf.RoundToInt(Mathf.Lerp(2f, 6f, strength));
        for (int i = 0; i < puffs; i++)
        {
            float spread = i / (float)(puffs - 1);
            var drift = car.forward * Mathf.Lerp(-1f, speed * .12f, spread) +
                car.right * (side * (.55f + spread)) + Vector3.up * Mathf.Lerp(.3f, 1.0f, spread);
            EmitTrackSmoke(ref bodyContactSmoke, "Body contact smoke", origin, drift,
                new Color(.76f, .75f, .74f, Mathf.Lerp(.31f, .49f, strength)), false,
                Mathf.Lerp(.13f, .29f, strength));
        }

        if (bodyContactSparks == null)
        {
            if (!LoadFireworkMaterial()) return;
            bodyContactSparks = CreateTrackSparkEmitter("Body contact sparks", transform, 160, .85f,
                ParticleSystemRenderMode.Stretch);
            var renderer = bodyContactSparks.GetComponent<ParticleSystemRenderer>();
            renderer.velocityScale = .018f;
            renderer.lengthScale = 2f;
        }
        int sparks = Mathf.RoundToInt(Mathf.Lerp(7f, 19f, strength));
        for (int i = 0; i < sparks; i++)
        {
            float spread = i / (float)(sparks - 1);
            bodyContactSparks.Emit(new ParticleSystem.EmitParams
            {
                position = origin,
                velocity = car.forward * (speed * .6f - Mathf.Lerp(3f, 9f, spread)) +
                    car.right * (side * Mathf.Lerp(.3f, 2.6f, spread)) + Vector3.up * (.5f + (i % 5) * .3f),
                startColor = Color.Lerp(new Color(1f, .88f, .57f), new Color(1f, .40f, .12f), spread),
                startSize = Mathf.Lerp(.05f, .023f, spread),
                startLifetime = Mathf.Lerp(.21f, .11f, spread)
            }, 1);
        }
    }
}
