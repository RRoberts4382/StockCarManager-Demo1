using System;
using UnityEngine;

internal sealed class VehicleCollision001
{
    internal const float ContactLength = 2.9f;
    internal const float ContactWidth = 1.8f;
    private readonly bool[,] touching;
    private readonly float[] impactDamage;
    private readonly float[] crashImpulse;

    public VehicleCollision001(int count)
    {
        touching = new bool[count, count];
        impactDamage = new float[count];
        crashImpulse = new float[count];
    }

    public void ClearContacts() => Array.Clear(touching, 0, touching.Length);

    public float CrashImpulse(int index) => crashImpulse[index];

    public bool HadImpact(int index) => impactDamage[index] > 0f;

    public void Resolve(RaceSessionRun run, float lapLength, float seconds,
        bool[] onTrack, float[] startDistances, float[] endDistances, float[] startLanes, float[] endLanes, bool wrap = true, float[] startYaw = null, float[] endYaw = null)
    {
        if (seconds <= 0f)
            return;

        Array.Clear(impactDamage, 0, impactDamage.Length);
        Array.Clear(crashImpulse, 0, crashImpulse.Length);
        for (int first = 0; first < onTrack.Length; first++)
        {
            for (int second = first + 1; second < onTrack.Length; second++)
            {
                if (!onTrack[first] || !onTrack[second])
                {
                    touching[first, second] = false;
                    continue;
                }

                float gap = startDistances[second] - startDistances[first];
                if (wrap)
                    gap = Mathf.Repeat(gap + lapLength * 0.5f, lapLength) - lapLength * 0.5f;
                float longitudinal = (endDistances[second] - startDistances[second]) -
                    (endDistances[first] - startDistances[first]);
                float side = startLanes[second] - startLanes[first];
                float lateral = endLanes[second] - endLanes[first] - side;
                float enter = 0f;
                float exit = 1f;
                bool oriented = startYaw != null && endYaw != null;
                bool contact;
                if (oriented)
                {
                    contact = false;
                    // Subdivide swept translation and yaw, retaining collision damage
                    // during stall maneuvers instead of exempting parked cars.
                    int samples = Mathf.Clamp(Mathf.CeilToInt((Mathf.Abs(longitudinal) + Mathf.Abs(lateral)) / .1f) + 4, 4, 64);
                    for (int sample = 0; sample <= samples && !contact; sample++)
                    {
                        float t = sample / (float)samples;
                        contact = PitFootprint.Overlaps(gap + longitudinal * t, side + lateral * t,
                            Mathf.LerpAngle(startYaw[first], endYaw[first], t), Mathf.LerpAngle(startYaw[second], endYaw[second], t));
                    }
                }
                else contact = OverlapsDuringStep(gap, longitudinal, ContactLength, ref enter, ref exit) &&
                    OverlapsDuringStep(side, lateral, ContactWidth, ref enter, ref exit);
                if (!touching[first, second] && contact)
                {
                    float relativeSpeed = Mathf.Sqrt(longitudinal * longitudinal + lateral * lateral) / seconds;
                    float damage = Mathf.Clamp(relativeSpeed * 1.2f, 0.5f, 100f);
                    run.RecordContact(first, second, damage);
                    // Hard contact breaks traction. Opposite impulses send the cars apart.
                    if (relativeSpeed >= 8f)
                    {
                        float direction = side >= 0f ? 1f : -1f;
                        float impulse = Mathf.Min(14f, relativeSpeed * 0.6f);
                        crashImpulse[first] -= direction * impulse;
                        crashImpulse[second] += direction * impulse;
                    }
                    impactDamage[first] += damage;
                    impactDamage[second] += damage;
                }

                // Small release margin avoids repeated impact damage from contact projection jitter.
                float releaseMargin = touching[first, second] || impactDamage[first] > 0f ? .05f : 0f;
                touching[first, second] = oriented
                    ? PitFootprint.Overlaps(gap + longitudinal, side + lateral, endYaw[first], endYaw[second], releaseMargin * .5f)
                    : Mathf.Abs(gap + longitudinal) < ContactLength + releaseMargin && Mathf.Abs(side + lateral) < ContactWidth + releaseMargin;
            }
        }

        for (int i = 0; i < impactDamage.Length; i++)
        {
            if (impactDamage[i] > 0f)
                run.ApplyDamage(i, impactDamage[i]);
        }
    }

    // Split pit routes do not share a distance coordinate. Use the actual
    // swept poses, including cars on different connectors and track/pit merges.
    internal void ResolveSpatial(RaceSessionRun run, float seconds, bool[] active, bool[] pitInvolved,
        Vector3[] start, Vector3[] end, float[] yawStart, float[] yawEnd)
    {
        Array.Clear(impactDamage, 0, impactDamage.Length);
        Array.Clear(crashImpulse, 0, crashImpulse.Length);
        if (seconds <= 0f) return;
        for (int i = 0; i < active.Length; i++) for (int j = i + 1; j < active.Length; j++)
        {
            if (!active[i] || !active[j] || !(pitInvolved[i] || pitInvolved[j]))
            { touching[i, j] = false; continue; }
            bool contact = SpatialContact(start[i], end[i], yawStart[i], yawEnd[i], start[j], end[j], yawStart[j], yawEnd[j]);
            if (contact && !touching[i, j])
            {
                Vector3 relative = (end[j] - start[j]) - (end[i] - start[i]);
                float damage = Mathf.Clamp(relative.magnitude / seconds * 1.2f, .5f, 100f);
                run.RecordContact(i, j, damage);
                impactDamage[i] += damage; impactDamage[j] += damage;
            }
            Vector3 delta = end[j] - end[i];
            float margin = touching[i, j] || contact ? .025f : 0f;
            touching[i, j] = Mathf.Abs(delta.y) < 1.5f && PitFootprint.Overlaps(delta.z, delta.x, yawEnd[i], yawEnd[j], margin);
        }
        for (int i = 0; i < active.Length; i++) if (impactDamage[i] > 0f) run.ApplyDamage(i, impactDamage[i]);
    }

    internal static bool SpatialContact(Vector3 a0, Vector3 a1, float yawA0, float yawA1,
        Vector3 b0, Vector3 b1, float yawB0, float yawB1)
    {
        Vector3 delta0 = b0 - a0, change = (b1 - a1) - delta0;
        // Broad phase on the swept relative position; avoids sampling distant
        // cars with identical progress values on Bristol's opposite pit roads.
        Vector2 planar = new Vector2(delta0.x, delta0.z), motion = new Vector2(change.x, change.z);
        float closest = motion.sqrMagnitude > .000001f ? Mathf.Clamp01(-Vector2.Dot(planar, motion) / motion.sqrMagnitude) : 0f;
        if ((planar + motion * closest).sqrMagnitude > 12.25f) return false;
        float rotationTravel = (Mathf.Abs(Mathf.DeltaAngle(yawA0, yawA1)) + Mathf.Abs(Mathf.DeltaAngle(yawB0, yawB1))) * Mathf.Deg2Rad * 1.71f;
        int samples = Mathf.Clamp(Mathf.CeilToInt((motion.magnitude + rotationTravel) / .05f), 4, 256);
        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            Vector3 delta = delta0 + change * t;
            if (Mathf.Abs(delta.y) < 1.5f && PitFootprint.Overlaps(delta.z, delta.x,
                Mathf.LerpAngle(yawA0, yawA1, t), Mathf.LerpAngle(yawB0, yawB1, t))) return true;
        }
        return false;
    }

    // Position projection resolves penetration without changing commanded velocities.
    // Trailing cars move backward, never forward through another car or across a finish line.
    internal static void Separate(float lapLength, bool[] active, float[] distances, float[] lanes, float[] widths)
    {
        for (int pass = 0; pass < active.Length; pass++)
        {
            bool changed = false;
            for (int i=0;i<active.Length;i++) for(int j=i+1;j<active.Length;j++)
            {
                if (!active[i] || !active[j]) continue;
                float gap = Mathf.Repeat(distances[j]-distances[i]+lapLength*.5f,lapLength)-lapLength*.5f;
                float side = lanes[j]-lanes[i];
                float longitudinal = ContactLength-Mathf.Abs(gap);
                float lateral = ContactWidth-Mathf.Abs(side);
                if (longitudinal <= 0f || lateral <= 0f) continue;
                float direction = side >= 0f ? 1f : -1f;
                float halfI = Mathf.Max(0f,widths[i]*.5f-ContactWidth*.5f);
                float halfJ = Mathf.Max(0f,widths[j]*.5f-ContactWidth*.5f);
                float moveI = Mathf.Clamp(lanes[i]-direction*(lateral+.002f)*.5f,-halfI,halfI);
                float moveJ = Mathf.Clamp(lanes[j]+direction*(lateral+.002f)*.5f,-halfJ,halfJ);
                if (lateral < longitudinal && Mathf.Abs(moveJ-moveI) >= ContactWidth)
                { lanes[i]=moveI; lanes[j]=moveJ; }
                else if (gap >= 0f) distances[i] -= longitudinal+.002f;
                else distances[j] -= longitudinal+.002f;
                changed = true;
            }
            if (!changed) break;
        }
    }

    private static bool OverlapsDuringStep(float separation, float change, float limit, ref float enter, ref float exit)
    {
        if (Mathf.Abs(change) < 0.00001f)
            return Mathf.Abs(separation) < limit;

        float first = (-limit - separation) / change;
        float second = (limit - separation) / change;
        enter = Mathf.Max(enter, Mathf.Min(first, second));
        exit = Mathf.Min(exit, Mathf.Max(first, second));
        return enter < exit;
    }
}
