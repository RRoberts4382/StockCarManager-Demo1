using System;
using UnityEngine;

[Flags]
internal enum SpotterOverlap { None = 0, Low = 1, High = 2, Both = Low | High }

internal enum SpotterCall
{
    None, Inside, Outside, Left, Right, Middle, ClearLow, ClearHigh, ClearLeft, ClearRight, ClearBoth,
    StillInside, StillOutside, StillLeft, StillRight, StillMiddle, Closing, Wreck, Slow, Green, Yellow, Merge, PitMerge
}

internal struct SpotterTraffic
{
    internal bool Green, Yellow, RoadCourse, OnTrack, OnApron, MergeTraffic, Closing;
    internal SpotterOverlap Overlap;
    internal SpotterCall Hazard;
}

internal sealed class PlayerSpotter
{
    private RaceSessionRun session;
    private bool wasGreen, wasYellow, wasWaitingForMerge, wasClosing;
    private SpotterCall reportedHazard;
    private SpotterOverlap observedOverlap, calledOverlap;
    private double nextSample, overlapChangedAt, nextReminder, nextClosing;

    internal SpotterCall Observe(TrackPreview track, RaceSessionRun run, GameSettings.SpotterCallFrequency frequency)
    {
        if (session != run)
        {
            session = run;
            wasGreen = wasYellow = wasWaitingForMerge = wasClosing = false;
            reportedHazard = SpotterCall.None;
            observedOverlap = calledOverlap = SpotterOverlap.None;
            nextSample = overlapChangedAt = nextReminder = nextClosing = 0d;
        }

        double now = run.PreciseElapsed;
        if (now < nextSample)
            return SpotterCall.None;
        nextSample = now + .10d;
        SpotterTraffic traffic = track.SampleSpotterTraffic();
        bool yellowStarted = traffic.Yellow && !wasYellow;
        bool greenStarted = traffic.Green && !wasGreen;
        bool mergeStarted = traffic.MergeTraffic && !wasWaitingForMerge;
        bool closingStarted = traffic.Closing && !wasClosing;
        wasYellow = traffic.Yellow;
        wasGreen = traffic.Green;
        wasWaitingForMerge = traffic.MergeTraffic;
        wasClosing = traffic.Closing;

        if (!traffic.OnTrack)
            observedOverlap = calledOverlap = SpotterOverlap.None;
        if (traffic.Hazard == SpotterCall.None)
            reportedHazard = SpotterCall.None;
        if (yellowStarted)
            return SpotterCall.Yellow;
        if (traffic.Hazard != reportedHazard)
        {
            reportedHazard = traffic.Hazard;
            return reportedHazard;
        }
        if (greenStarted && run.IsRace)
            return SpotterCall.Green;
        if (mergeStarted)
            return traffic.OnApron ? SpotterCall.Merge : SpotterCall.PitMerge;
        if (!traffic.OnTrack)
            return SpotterCall.None;

        if (traffic.Overlap != observedOverlap)
        {
            observedOverlap = traffic.Overlap;
            overlapChangedAt = now;
        }

        if (observedOverlap != calledOverlap)
        {
            SpotterOverlap entering = observedOverlap & ~calledOverlap;
            if (entering != SpotterOverlap.None || now - overlapChangedAt >= .22d)
            {
                SpotterOverlap leaving = calledOverlap & ~observedOverlap;
                calledOverlap = observedOverlap;
                nextReminder = now + ReminderInterval(frequency);
                if (entering != SpotterOverlap.None)
                    return Alongside(calledOverlap, traffic.RoadCourse, repeated: false);
                return leaving switch
                {
                    SpotterOverlap.Both => SpotterCall.ClearBoth,
                    SpotterOverlap.Low => traffic.RoadCourse ? SpotterCall.ClearLeft : SpotterCall.ClearLow,
                    _ => traffic.RoadCourse ? SpotterCall.ClearRight : SpotterCall.ClearHigh
                };
            }
        }

        if (frequency != GameSettings.SpotterCallFrequency.Low &&
            calledOverlap != SpotterOverlap.None && observedOverlap == calledOverlap && now >= nextReminder)
        {
            nextReminder = now + ReminderInterval(frequency);
            return Alongside(calledOverlap, traffic.RoadCourse, repeated: true);
        }

        if (frequency == GameSettings.SpotterCallFrequency.High &&
            closingStarted && calledOverlap == SpotterOverlap.None && now >= nextClosing)
        {
            nextClosing = now + 8d;
            return SpotterCall.Closing;
        }

        return SpotterCall.None;
    }

    private static double ReminderInterval(GameSettings.SpotterCallFrequency frequency) =>
        frequency == GameSettings.SpotterCallFrequency.High ? 2.2d : 4d;

    private static SpotterCall Alongside(SpotterOverlap overlap, bool roadCourse, bool repeated) => overlap switch
    {
        SpotterOverlap.Both => repeated ? SpotterCall.StillMiddle : SpotterCall.Middle,
        SpotterOverlap.Low when roadCourse => repeated ? SpotterCall.StillLeft : SpotterCall.Left,
        SpotterOverlap.Low => repeated ? SpotterCall.StillInside : SpotterCall.Inside,
        _ when roadCourse => repeated ? SpotterCall.StillRight : SpotterCall.Right,
        _ => repeated ? SpotterCall.StillOutside : SpotterCall.Outside
    };

    internal static int Priority(SpotterCall call) => call switch
    {
        SpotterCall.Wreck or SpotterCall.Slow or SpotterCall.Yellow or SpotterCall.Green or
            SpotterCall.Merge or SpotterCall.PitMerge => 2,
        SpotterCall.StillInside or SpotterCall.StillOutside or SpotterCall.StillLeft or SpotterCall.StillRight or
            SpotterCall.StillMiddle or SpotterCall.Closing => 0,
        _ => 1
    };

    internal static string Text(SpotterCall call) => call switch
    {
        SpotterCall.Inside => "Inside.",
        SpotterCall.Outside => "Outside.",
        SpotterCall.Left => "Car left.",
        SpotterCall.Right => "Car right.",
        SpotterCall.Middle => "Three wide, you're in the middle. Hold your line.",
        SpotterCall.ClearLow => "Clear low.",
        SpotterCall.ClearHigh => "Clear high.",
        SpotterCall.ClearLeft => "Clear left.",
        SpotterCall.ClearRight => "Clear right.",
        SpotterCall.ClearBoth => "Clear all around.",
        SpotterCall.StillInside => "Still inside.",
        SpotterCall.StillOutside => "Still outside.",
        SpotterCall.StillLeft => "Still there, left side.",
        SpotterCall.StillRight => "Still there, right side.",
        SpotterCall.StillMiddle => "Still three wide.",
        SpotterCall.Closing => "Car closing fast behind you.",
        SpotterCall.Wreck => "Wreck ahead! Check up!",
        SpotterCall.Slow => "Slow car ahead! Check up!",
        SpotterCall.Green => "Green, green, green!",
        SpotterCall.Yellow => "Yellow's out. Check up.",
        SpotterCall.Merge => "Hold the apron. Traffic coming.",
        SpotterCall.PitMerge => "Hold there. Traffic coming.",
        _ => string.Empty
    };
}

public sealed partial class TrackPreview
{
    internal SpotterTraffic SampleSpotterTraffic()
    {
        bool roadCourse = definition.majorStyle == "Road";
        var sample = new SpotterTraffic
        {
            Green = IsUnderGreen,
            Yellow = IsUnderCaution,
            RoadCourse = roadCourse
        };
        var player = qualifying?.Car ?? trafficCars?[0];
        var run = SessionRun;
        if (player == null || run == null || run.Retired(0) || run.Finished(0) || player.InGarage)
            return sample;

        sample.OnApron = player.ApronExitActive;
        sample.MergeTraffic = player.WaitingForExitMerge ||
            (sample.OnApron && player.ApronExitInitialized && !player.ApronMergeGranted &&
             player.TrackDistance >= player.ApronMergeStart);
        if (player.InPit || sample.OnApron || player.ApronEntryActive || trafficCars == null || crashes[0].Active ||
            (!sample.Green && !sample.Yellow))
            return sample;

        float speed = run.CurrentSpeed(0);
        if (speed < 3f)
            return sample;

        sample.OnTrack = true;
        float lane = drivingLanes[0];
        float lowSide = roadCourse ? -1f : customLayout?.InsideSign ?? -1f;
        float hazardDistance = Mathf.Clamp(speed * 2.5f, 25f, 180f);
        for (int i = 1; i < trafficCars.Length; i++)
        {
            var other = trafficCars[i];
            if (other == null || other.InGarage || other.InPit || other.ApronExitActive || other.ApronEntryActive ||
                !cars[i].gameObject.activeSelf)
                continue;

            float ahead = Mathf.Repeat(other.TrackDistance - player.TrackDistance + lapLength * .5f, lapLength) -
                lapLength * .5f;
            float lateral = drivingLanes[i] - lane;
            float laneSeparation = Mathf.Abs(lateral);
            float width = VehicleCollision001.TrackContactWidth(raceCarHalfWidths, 0, i);
            float otherSpeed = run.CurrentSpeed(i);
            if (ahead > 0f && ahead < hazardDistance &&
                Mathf.Abs(drivingLanes[i]) <= RoadWidths(other.TrackDistance).x * .5f)
            {
                if (crashes[i].Active)
                    sample.Hazard = SpotterCall.Wreck;
                else if (sample.Hazard == SpotterCall.None && laneSeparation < width + .65f &&
                    otherSpeed < 8f && speed - otherSpeed > 10f && ahead < (speed - otherSpeed) * 1.8f)
                    sample.Hazard = SpotterCall.Slow;
            }

            if (run.Retired(i) || crashes[i].Active)
                continue;
            if (Mathf.Abs(ahead) <= VehicleCollision001.ContactLength + .65f &&
                laneSeparation > width * .5f && laneSeparation < width * 1.8f)
                sample.Overlap |= lateral * lowSide > 0f ? SpotterOverlap.Low : SpotterOverlap.High;
            if (sample.Green && ahead < -7f && ahead > -35f && laneSeparation < width &&
                otherSpeed - speed > 6f)
                sample.Closing = true;
        }

        return sample;
    }
}
