using UnityEngine;

internal sealed class RestrictorPlateDraft
{
    private const float MphToMetersPerSecond = TrackDefinition.MetersPerMile / 3600f;
    private const float WakeRange = 65f * TrackDefinition.MetersPerFoot;
    private const float JoinRange = 120f * TrackDefinition.MetersPerFoot;
    private const float Alignment = 3f * TrackDefinition.MetersPerFoot;
    private readonly float[] boost;
    private readonly bool[] active;
    private readonly bool[] inLine;

    public RestrictorPlateDraft(int count)
    {
        boost = new float[count];
        active = new bool[count];
        inLine = new bool[count];
    }

    public float SpeedBoost(int index) => boost[index];
    public bool[] InLine => inLine;

    public void Update(float lapLength, float seconds, RaceSessionRun run, bool[] onTrack,
        float[] distances, float[] widths, float[] lanes, float[] targets, float[] speeds, float[] pitApproach)
    {
        for (int i = 0; i < active.Length; i++)
        {
            active[i] = onTrack[i] && !run.Finished(i) && speeds[i] >= 65f * MphToMetersPerSecond &&
                pitApproach[i] > 15f;
        }

        for (int i = 0; i < active.Length; i++)
        {
            float frontWake = 0f;
            float rearWake = 0f;
            float bestLine = 0f;
            int partner = -1;
            if (active[i])
            {
                for (int j = 0; j < active.Length; j++)
                {
                    if (i == j || !active[j])
                        continue;

                    float gap = Mathf.Repeat(distances[j] - distances[i] + lapLength * 0.5f, lapLength)
                        - lapLength * 0.5f;
                    float lateral = Mathf.Abs(lanes[j] - lanes[i]);
                    float separation = Mathf.Max(0f, Mathf.Abs(gap) - 2.9f);
                    if (lateral < Alignment && separation < WakeRange && gap != 0f)
                    {
                        float wake = (1f - separation / WakeRange) * (1f - 0.35f * lateral / Alignment);
                        if (gap > 0f)
                            frontWake = Mathf.Max(frontWake, wake);
                        else
                            rearWake = Mathf.Max(rearWake, wake);
                    }

                    if (gap <= 2.9f || gap > JoinRange || lateral > 3.5f)
                        continue;

                    float score = (1f - gap / JoinRange) * (1f - 0.65f * lateral / 3.5f);
                    if (score > bestLine)
                    {
                        bestLine = score;
                        partner = j;
                    }
                }
            }

            inLine[i] = frontWake > 0f;
            float targetBoost = Mathf.Max(5f * frontWake, 4f * rearWake) * MphToMetersPerSecond;
            boost[i] = Mathf.MoveTowards(boost[i], targetBoost, (targetBoost > boost[i] ? 3f : 5f) * MphToMetersPerSecond * seconds);
            if (partner >= 0)
            {
                float edge = Mathf.Max(0f, widths[i] * 0.5f - 1.1f);
                targets[i] = Mathf.Clamp(lanes[partner], -edge, edge);
            }
        }
    }
}
