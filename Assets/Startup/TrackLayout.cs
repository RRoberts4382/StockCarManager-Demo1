using System;
using UnityEngine;

public sealed class TrackLayout
{
    private const int SamplesPerSegment = 24;
    private readonly Vector3[] points;
    private readonly float[] banks;
    private readonly double[] bankRates;
    private readonly float[] distances;
    private readonly float scale;
    private readonly Vector3 sourceCenter;
    private readonly TrackLayoutPoint[] controls;
    private readonly float defaultWidth;
    private readonly float lapLength;
    private readonly TrackBankingTransition[] bankingTransitions;

    public int Samples => points.Length;
    internal bool HasContinuousBanking => banks == null;
    public float InsideSign { get; }
    internal float MinimumElevation { get; }
    internal bool HasElevation => SourceSize.y > 0f || MinimumElevation != 0f;
    internal Vector3 SourceSize { get; }
    internal float SourceCenterX => sourceCenter.x;

    public TrackLayout(TrackLayoutPoint[] layout, float lapLength, float defaultWidth = 9f)
        : this(layout, lapLength, defaultWidth, null)
    {
    }

    public TrackLayout(TrackLayoutPoint[] layout, float lapLength, float defaultWidth,
        TrackBankingTransition[] bankingTransitions)
        : this(layout, lapLength, defaultWidth, bankingTransitions, false)
    {
    }

    public TrackLayout(TrackLayoutPoint[] layout, float lapLength, float defaultWidth,
        TrackBankingTransition[] bankingTransitions, bool smoothLayoutBanking)
    {
        controls = layout;
        this.defaultWidth = defaultWidth;
        this.lapLength = lapLength;
        this.bankingTransitions = bankingTransitions;
        points = new Vector3[layout.Length * SamplesPerSegment];
        bool hasTransitions = bankingTransitions?.Length > 0;
        if (!hasTransitions && !smoothLayoutBanking)
            banks = new float[points.Length];
        distances = new float[points.Length + 1];
        for (int i = 0; i < layout.Length; i++)
        {
            Vector3 a = Point(layout[(i + layout.Length - 1) % layout.Length]);
            Vector3 b = Point(layout[i]);
            Vector3 c = Point(layout[(i + 1) % layout.Length]);
            Vector3 d = Point(layout[(i + 2) % layout.Length]);
            for (int j = 0; j < SamplesPerSegment; j++)
            {
                int index = i * SamplesPerSegment + j;
                float t = (float)j / SamplesPerSegment;
                points[index] = 0.5f * (2f * b + (c - a) * t
                    + (2f * a - 5f * b + 4f * c - d) * (t * t)
                    + (-a + 3f * b - 3f * c + d) * (t * t * t));
                points[index].y = Mathf.Clamp(points[index].y, Mathf.Min(b.y, c.y), Mathf.Max(b.y, c.y));
                if (banks != null)
                    banks[index] = Mathf.SmoothStep(layout[i].banking, layout[(i + 1) % layout.Length].banking, t);
            }
        }

        Vector3 min = points[0];
        Vector3 max = points[0];
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 segment = points[(i + 1) % points.Length] - points[i];
            // Keep the existing plan-view distances for lap timing and track markers.
            distances[i + 1] = distances[i] + new Vector2(segment.x, segment.z).magnitude;
            min = Vector3.Min(min, points[i]);
            max = Vector3.Max(max, points[i]);
        }
        if (!hasTransitions && smoothLayoutBanking)
            bankRates = LayoutBankRates();

        double area = 0;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[(i + 1) % points.Length];
            area += (double)a.x * b.z - (double)a.z * b.x;
        }
        InsideSign = area >= 0 ? -1f : 1f;
        scale = lapLength / distances[points.Length];
        sourceCenter = (min + max) * 0.5f;
        SourceSize = max - min;
        MinimumElevation = min.y;
        for (int i = 0; i < points.Length; i++)
            points[i] = ToTrackSpace(points[i]);
    }

    public float SampleDistance(int index) => distances[index] * scale;

    public Vector3 ToTrackSpace(Vector3 source) =>
        new Vector3((source.x - sourceCenter.x) * scale, source.y, (source.z - sourceCenter.z) * scale);
    public Vector3 ToSourceSpace(Vector3 position) =>
        new Vector3(position.x / scale + sourceCenter.x, position.y, position.z / scale + sourceCenter.z);

    public float ControlFraction(int index) => distances[index * SamplesPerSegment] / distances[points.Length];

    public float BankingSpread(float distance)
    {
        int index = Segment(distance, out float t);
        int control = index / SamplesPerSegment;
        float blend = (index % SamplesPerSegment + t) / SamplesPerSegment;
        return Mathf.Lerp(controls[control].bankingSpread, controls[(control + 1) % controls.Length].bankingSpread, blend);
    }

    public Vector2 Widths(float distance)
    {
        int index = Segment(distance, out float t);
        int control = index / SamplesPerSegment;
        float blend = (index % SamplesPerSegment + t) / SamplesPerSegment;
        var a = controls[control];
        var b = controls[(control + 1) % controls.Length];
        return new Vector2(
            Mathf.SmoothStep(a.widthMeters > 0f ? a.widthMeters : defaultWidth,
                b.widthMeters > 0f ? b.widthMeters : defaultWidth, blend),
            Mathf.SmoothStep(a.apronWidthMeters, b.apronWidthMeters, blend));
    }

    private int Segment(float distance, out float t)
    {
        float unscaled = distance / scale;
        int index = Array.BinarySearch(distances, unscaled);
        index = Mathf.Clamp(index < 0 ? ~index - 1 : index, 0, points.Length - 1);
        t = Mathf.InverseLerp(distances[index], distances[index + 1], unscaled);
        return index;
    }

    public Vector3 Sample(float distance, out Vector3 forward, out float bank)
    {
        int index = Segment(distance, out float t);
        int next = (index + 1) % points.Length;
        int previous = (index + points.Length - 1) % points.Length;
        int afterNext = (next + 1) % points.Length;
        forward = Vector3.Lerp((points[next] - points[previous]).normalized,
            (points[afterNext] - points[index]).normalized, t).normalized;
        if (forward == Vector3.zero)
            forward = (points[next] - points[index]).normalized;
        if (banks != null)
            bank = Mathf.Lerp(banks[index], banks[next], t);
        else if (bankRates != null)
            bank = SampleLayoutBank(index, t);
        else
            bank = SampleBanking(distance);
        return Vector3.Lerp(points[index], points[next], t);
    }

    private double[] LayoutBankRates()
    {
        var rates = new double[controls.Length];
        for (int i = 0; i < rates.Length; i++)
        {
            int previous = (i + rates.Length - 1) % rates.Length;
            int next = (i + 1) % rates.Length;
            double before = ControlDistance(i) - ControlDistance(previous);
            if (i == 0)
                before += ControlDistance(rates.Length);
            double after = ControlDistance(i + 1) - ControlDistance(i);
            if (before <= 0d || after <= 0d)
                continue;

            double incoming = ((double)controls[i].banking - controls[previous].banking) / before;
            double outgoing = ((double)controls[next].banking - controls[i].banking) / after;
            if (incoming * outgoing <= 0d)
                continue;

            // The weighted harmonic mean shares one rate across a knot and
            // preserves monotonicity. Flat intervals and extrema keep zero rate.
            double firstWeight = 2d * after + before;
            double secondWeight = after + 2d * before;
            rates[i] = (firstWeight + secondWeight) / (firstWeight / incoming + secondWeight / outgoing);
        }
        return rates;
    }

    private float SampleLayoutBank(int sample, float fraction)
    {
        int control = sample / SamplesPerSegment;
        int next = (control + 1) % controls.Length;
        double start = ControlDistance(control);
        double length = ControlDistance(control + 1) - start;
        if (length <= 0d)
            return controls[control].banking;

        double delta = distances[sample] - start + (double)fraction * (distances[sample + 1] - distances[sample]);
        double t = delta / length;
        double remaining = 1d - t;
        double nextWeight = t * t * (3d - 2d * t);
        return (float)((1d - nextWeight) * controls[control].banking + nextWeight * controls[next].banking +
            length * t * remaining * (remaining * bankRates[control] - t * bankRates[next]));
    }

    private double ControlDistance(int index) => distances[index * SamplesPerSegment];

    private float SampleBanking(float distance)
    {
        float fraction = Mathf.Clamp01(distance / lapLength);
        foreach (var transition in bankingTransitions)
        {
            if (fraction < transition.start)
                return transition.fromDegrees;
            if (fraction <= transition.end)
            {
                // Apply one quintic across the whole transition, with zero rates
                // and accelerations at its endpoints. Hold bank between transitions.
                double t = ((double)fraction - transition.start) / (transition.end - transition.start);
                float blend = (float)(t * t * t * (10d + t * (-15d + 6d * t)));
                return Mathf.Lerp(transition.fromDegrees, transition.toDegrees, blend);
            }
        }
        return bankingTransitions[bankingTransitions.Length - 1].toDegrees;
    }

    private static Vector3 Point(TrackLayoutPoint point) => new Vector3(point.x, point.elevationMeters, point.z);
}
