using System;
using UnityEngine;

internal sealed class VehicleCollision001
{
    private const float ContactLength = 2.9f;
    private const float ContactWidth = 1.8f;
    private readonly bool[,] touching;
    private readonly float[] impactDamage;
    private readonly float[] crashImpulse;

    public VehicleCollision001(int count)
    {
        touching = new bool[count, count];
        impactDamage = new float[count];
        crashImpulse = new float[count];
    }

    public float CrashImpulse(int index) => crashImpulse[index];

    public bool HadImpact(int index) => impactDamage[index] > 0f;

    public void Resolve(RaceSessionRun run, float lapLength, float seconds,
        bool[] onTrack, float[] startDistances, float[] endDistances, float[] startLanes, float[] endLanes, bool wrap = true)
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
                if (!touching[first, second] &&
                    OverlapsDuringStep(gap, longitudinal, ContactLength, ref enter, ref exit) &&
                    OverlapsDuringStep(side, lateral, ContactWidth, ref enter, ref exit))
                {
                    float relativeSpeed = Mathf.Sqrt(longitudinal * longitudinal + lateral * lateral) / seconds;
                    float damage = Mathf.Clamp(relativeSpeed * 1.2f, 0.5f, 100f);
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

                touching[first, second] = Mathf.Abs(gap + longitudinal) < ContactLength &&
                    Mathf.Abs(side + lateral) < ContactWidth;
            }
        }

        for (int i = 0; i < impactDamage.Length; i++)
        {
            if (impactDamage[i] > 0f && run.Vehicle(i) is TeamCar car)
                car.Damage += impactDamage[i];
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
