using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class TrafficStabilityProbe
{
    private const float Step = 1f / 60f;
    private const float LapLength = 857.7803f;
    private const float RoadWidth = 12.192f;
    private const float Clearance = 2.01f;
    private const float BodyWidth = 1.8f;
    private const float OuterLane = 4.097f;
    private const float BottomLane = -5.386f;
    private const BindingFlags InternalInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [DidReloadScripts]
    private static void ScheduleAfterScriptReload()
    {
        if (!Application.isBatchMode)
            EditorApplication.delayCall += RunAfterScriptReload;
    }

    private static void RunAfterScriptReload()
    {
        try
        {
            if (!RecordControllerChange(Application.dataPath))
                return;

            Run();
            Debug.Log("Traffic controller regression passed.");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
        }
    }

    private static bool RecordControllerChange(string dataPath)
    {
        string[] sources = Directory.GetFiles(Path.Combine(dataPath, "Startup"), "VehicleBehavior*.cs");
        Array.Sort(sources, StringComparer.Ordinal);
        using var sha256 = SHA256.Create();
        var fingerprint = new StringBuilder();
        foreach (string path in sources)
        {
            using var source = File.OpenRead(path);
            fingerprint.Append(Path.GetFileName(path)).Append(' ')
                .Append(Convert.ToBase64String(sha256.ComputeHash(source))).Append('\n');
        }

        string statePath = Path.Combine(dataPath, "..", "Library", "TrafficStabilityProbe.sources");
        string current = fingerprint.ToString();
        string previous = File.Exists(statePath) ? File.ReadAllText(statePath, Encoding.UTF8) : null;
        if (current == previous)
            return false;

        // Record the edit before running so unrelated reloads never retry a failed case.
        File.WriteAllText(statePath, current, new UTF8Encoding(false));
        return previous != null;
    }

    public static void Run()
    {
        EaseInwardBesideAnotherCar();
        EaseOutOfStartingFormation();
        HoldLaneBehindNearlyEqualCarAtGreen();
        RunCloselyAlongsideWithoutBraking();
        HoldPassSideUntilClear();
        ReturnPartwayAfterPassing();
        ReleaseStalledInsidePass();
        FollowPreferredLineOnClearTrack();
        ReleasePassToDraftLine();
    }

    private static void EaseInwardBesideAnotherCar()
    {
        // Bristol #38 closes on the inward line while the slightly faster #27
        // sits just ahead and moves inward at nearly the clearance threshold.
        var traffic = new TrafficFixture(OuterLane, OuterLane - Clearance);
        float lastLateralSpeed = 0f;
        float peakSpeedChange = 0f;
        for (int tick = 0; tick < 60; tick++)
        {
            float elapsed = tick * Step;
            traffic.Place(573f + 55f * elapsed, 2.5f + .25f * elapsed, 55f, 55.25f);
            traffic.Lanes[1] = OuterLane - Clearance - 1.6f * elapsed;
            traffic.Targets[1] = traffic.Lanes[1];
            traffic.Targets[0] = 2.3f - 2.5f * elapsed;
            traffic.LineAdvance[0] = -2.5f * Step;
            float previous = traffic.Lanes[0];
            traffic.Advance();
            float lateralSpeed = (previous - traffic.Lanes[0]) / Step;
            peakSpeedChange = Mathf.Max(peakSpeedChange, Mathf.Abs(lateralSpeed - lastLateralSpeed));
            lastLateralSpeed = lateralSpeed;
            Require(lateralSpeed > -.001f, $"Bristol #38 reversed its lane correction at tick {tick}.");
            Require(traffic.Lanes[0] - traffic.Lanes[1] >= BodyWidth - .001f,
                $"Bristol #38 crossed the swept lateral clearance at tick {tick}.");
        }

        float inwardTravel = OuterLane - traffic.Lanes[0];
        Require(inwardTravel > .6f, "Bristol #38 never resumed meaningful inward movement.");
        Require(peakSpeedChange < 2f,
            $"Bristol #38 still changed lateral speed by {peakSpeedChange:F3} m/s in one tick.");
        Debug.Log($"Bristol #38 advanced {inwardTravel:F3} m inward with at most {peakSpeedChange:F3} m/s lateral speed change per tick.");
    }

    private static void EaseOutOfStartingFormation()
    {
        // Bristol #66 moved outward as the starting row released, with #50
        // alongside near the lateral clearance limit.
        const float gridLane = 1.35f;
        var traffic = new TrafficFixture(-gridLane, gridLane);
        float lastLateralSpeed = 0f;
        float peakSpeedChange = 0f;
        for (int tick = 1; tick <= 144; tick++)
        {
            float elapsed = tick * Step;
            float speed = 32f + 2.5f * elapsed;
            traffic.Place(21.2f + 32f * elapsed + 1.25f * elapsed * elapsed, .01f, speed, speed);
            traffic.Lanes[1] = gridLane + .88f * elapsed;
            traffic.Targets[1] = traffic.Lanes[1];
            traffic.Targets[0] = Mathf.Lerp(-gridLane, OuterLane, elapsed / 3f);
            float previous = traffic.Lanes[0];
            traffic.Advance();
            float lateralSpeed = (traffic.Lanes[0] - previous) / Step;
            if (tick > 1)
                peakSpeedChange = Mathf.Max(peakSpeedChange, Mathf.Abs(lateralSpeed - lastLateralSpeed));
            lastLateralSpeed = lateralSpeed;
            Require(lateralSpeed >= -.001f, $"Bristol #66 reversed its opening lane correction at tick {tick}.");
            Require(traffic.Lanes[1] - traffic.Lanes[0] >= BodyWidth - .001f,
                $"Bristol #66 crossed #50’s lateral clearance at tick {tick}.");
        }

        float outwardTravel = traffic.Lanes[0] + gridLane;
        Require(outwardTravel > 1.5f, "Bristol #66 never eased outward from its starting lane.");
        Require(peakSpeedChange < .75f,
            $"Bristol #66 still changed its opening lateral speed by {peakSpeedChange:F3} m/s in one tick.");
        Debug.Log($"Bristol #66 advanced {outwardTravel:F3} m outward from the green flag with at most " +
            $"{peakSpeedChange:F3} m/s lateral speed change per tick.");
    }

    private static void HoldLaneBehindNearlyEqualCarAtGreen()
    {
        // Bristol #77 was only marginally faster than #91, 4.5 m ahead.
        // The opening target leads outward throughout the grid release.
        const float gridLane = -1.35f;
        var traffic = new TrafficFixture(gridLane, gridLane);
        traffic.Targets[1] = gridLane;
        for (int tick = 1; tick <= 180; tick++)
        {
            float elapsed = tick * Step;
            float speed = 32f + 2.5f * elapsed;
            traffic.Place(6.6f + 32f * elapsed + 1.25f * elapsed * elapsed,
                4.5f - .015f * elapsed, speed, speed - .015f);
            traffic.Targets[0] = Mathf.Lerp(gridLane, OuterLane, elapsed / 3f);
            float previous = traffic.Lanes[0];
            traffic.Advance();
            Require(traffic.Lanes[0] >= previous - .0001f,
                $"Bristol #77 turned inward behind a nearly equal-speed leader at tick {tick}.");
            Require(traffic.SubjectPassSide == 0,
                $"Bristol #77 committed to a pass with a negligible pace advantage at tick {tick}.");
        }
    }

    private static void RunCloselyAlongsideWithoutBraking()
    {
        // Stable parallel lanes fit the cars' 1.8 m collision envelope.
        const float lateralGap = 1.9f;
        const float pace = 40f;
        const float requestedSpeed = pace + .02f;
        var traffic = new TrafficFixture(0f, lateralGap);
        traffic.Targets[1] = lateralGap;
        for (int tick = 0; tick < 60; tick++)
        {
            float elapsed = tick * Step;
            traffic.Place(100f + requestedSpeed * elapsed, 2.6f - .02f * elapsed, requestedSpeed, pace);
            traffic.Advance();
            Require(Mathf.Abs(traffic.Lanes[0]) < .001f && Mathf.Abs(traffic.Lanes[1] - lateralGap) < .001f,
                $"Close parallel cars steered away from their lanes at tick {tick}.");
            Require(traffic.SubjectStepSpeed >= requestedSpeed - .001f,
                $"A close parallel car made its neighbor brake at tick {tick}.");
        }
    }

    private static void HoldPassSideUntilClear()
    {
        // Bristol #66 is faster than #99 and just outside its preferred path.
        // Start near the lap seam so wrapped progress cannot end the pass early.
        const float startingLane = -3.553f;
        const int openingTicks = 46;
        const float startingGap = 4.5f;
        const float openingClosingSpeed = 1.5f;
        const float passingClosingSpeed = 4f;
        var traffic = new TrafficFixture(startingLane, BottomLane);
        traffic.Targets[0] = BottomLane;
        traffic.Targets[1] = BottomLane;
        float establishedLane = startingLane;
        float firstInwardGap = float.NaN;
        bool passOpened = false;
        for (int tick = 0; tick < 270; tick++)
        {
            float openingElapsed = Mathf.Min(tick, openingTicks) * Step;
            float passingElapsed = Mathf.Max(0, tick - openingTicks) * Step;
            float gap = Mathf.Max(-8f, startingGap - openingClosingSpeed * openingElapsed - passingClosingSpeed * passingElapsed);
            float blockerSpeed = tick <= openingTicks ? 45.5f - openingClosingSpeed :
                gap > -8f ? 45.5f - passingClosingSpeed : 45.5f;
            traffic.Place(854f + 45.5f * tick * Step, gap, 45.5f, blockerSpeed);
            float previous = traffic.Lanes[0];
            traffic.Advance();
            if (tick < 10)
                Require(traffic.SubjectPassSide == 0, $"Bristol #66 committed before confirming the pace advantage at tick {tick}.");
            if (passOpened && float.IsNaN(firstInwardGap) && traffic.Lanes[0] < previous - .0001f)
                firstInwardGap = gap;
            passOpened |= traffic.Lanes[0] > startingLane + .15f;
            if (tick == openingTicks)
            {
                Require(traffic.SubjectPassSide != 0, "Bristol #66 lost its accepted pass after making lateral room.");
                establishedLane = traffic.Lanes[0];
            }
            if (Mathf.Abs(gap) <= 2.9f)
                Require(traffic.Lanes[0] - traffic.Lanes[1] >= BodyWidth - .001f,
                    $"Bristol #66 crossed the blocker’s lateral clearance at a {gap:F3} m lead.");
        }

        Require(establishedLane > startingLane + .15f, "Bristol #66 never established an outside pass.");
        Require(firstInwardGap < -2.9f,
            $"Bristol #66 turned back toward the occupied line at a {firstInwardGap:F3} m blocker lead.");
        Require(Mathf.Abs(traffic.Lanes[0] - BottomLane) < .02f,
            "Bristol #66 never returned to the preferred line after completing the pass.");
        Debug.Log($"Bristol #66 established the pass at {establishedLane:F3} m and began returning with the blocker {-firstInwardGap:F3} m behind.");
    }

    private static void FollowPreferredLineOnClearTrack()
    {
        var traffic = new TrafficFixture(OuterLane, BottomLane);
        traffic.Place(573f, 30f, 55f, 55f);
        traffic.Targets[0] = 2.3f;
        traffic.Targets[1] = BottomLane;
        traffic.LineAdvance[0] = -2.5f * Step;
        traffic.Advance();
        float expected = OuterLane - (3f + 2.5f) * Step;
        Require(Mathf.Abs(traffic.Lanes[0] - expected) < .00001f,
            "Traffic stabilization changed the clear-track 006 line advance.");
    }

    private static void ReturnPartwayAfterPassing()
    {
        // The pass is longitudinally clear, but another car overlaps alongside
        // near the requested line. The lane between them is still usable.
        const float insidePassLane = -5.046f;
        const float preferred = 3.455f;
        const float outsideLane = 3.881f;
        const float pace = 54f;
        const int blockedTicks = 240;
        var traffic = new TrafficFixture(insidePassLane, -2.88f, outsideLane);
        traffic.Targets[0] = preferred;
        traffic.Targets[1] = traffic.Lanes[1];
        traffic.Targets[2] = outsideLane;
        traffic.CommitSubjectPass(1, 1);

        float lastLateralSpeed = 0f;
        float peakSpeedChange = 0f;
        float lastSecondStart = insidePassLane;
        for (int tick = 0; tick < blockedTicks; tick++)
        {
            float distance = 120f + pace * tick * Step;
            traffic.Place(distance, -6f, pace, pace);
            traffic.PlaceCar(2, distance - 1.744f, pace);
            float previous = traffic.Lanes[0];
            traffic.Advance();
            float lateralSpeed = (traffic.Lanes[0] - previous) / Step;
            peakSpeedChange = Mathf.Max(peakSpeedChange, Mathf.Abs(lateralSpeed - lastLateralSpeed));
            lastLateralSpeed = lateralSpeed;
            if (tick == blockedTicks - 60)
                lastSecondStart = traffic.Lanes[0];
            Require(lateralSpeed >= -.001f, $"Bristol #40 reversed during a partial return at tick {tick}.");
            Require(traffic.SubjectPassSide >= 0, $"Bristol #40 switched to an outside pass while returning at tick {tick}.");
            Require(traffic.Lanes[2] - traffic.Lanes[0] >= BodyWidth - .001f,
                $"Bristol #40 crossed the alongside car's body clearance at tick {tick}.");
        }

        float partialLane = traffic.Lanes[0];
        Require(partialLane >= outsideLane - BodyWidth - .65f,
            $"Bristol #40 stopped at {partialLane:F3} m with a safe part of the return lane still open.");
        Require(partialLane - lastSecondStart < .10f && peakSpeedChange < 1f,
            $"Bristol #40 failed to settle smoothly near the alongside car (peak lateral speed change {peakSpeedChange:F3} m/s).");

        for (int tick = blockedTicks; tick < blockedTicks + 180; tick++)
        {
            float distance = 120f + pace * tick * Step;
            traffic.Place(distance, -6f, pace, pace);
            traffic.PlaceCar(2, distance - 8f, pace);
            traffic.Advance();
        }
        Require(Mathf.Abs(traffic.Lanes[0] - preferred) < .02f,
            "Bristol #40 never finished returning after the alongside car cleared.");
        Debug.Log($"Bristol #40 returned partway to {partialLane:F3} m while alongside, then reached the preferred line.");
    }

    private static void ReleaseStalledInsidePass()
    {
        // The blocker has moved onto the same inside lane as the committed car.
        // It is pulling away, but remains inside the old pass-clear distance.
        const float insidePassLane = -5.046f;
        const float pace = 54f;
        const float pullAwaySpeed = .35f;
        var traffic = new TrafficFixture(insidePassLane, insidePassLane);
        traffic.Targets[0] = 3.455f;
        traffic.Targets[1] = insidePassLane;
        traffic.CommitSubjectPass(1, 1);
        for (int tick = 0; tick < 240; tick++)
        {
            float elapsed = tick * Step;
            traffic.Place(120f + pace * elapsed, 6.29f + pullAwaySpeed * elapsed, pace, pace + pullAwaySpeed);
            float previous = traffic.Lanes[0];
            traffic.Advance();
            Require(traffic.Lanes[0] >= previous - .0001f,
                $"Bristol #40 steered back into a stalled inside pass at tick {tick}.");
            Require(traffic.SubjectPassSide >= 0, $"Bristol #40 switched pass sides while yielding at tick {tick}.");
        }

        Require(traffic.SubjectPassSide == 0 && traffic.Lanes[0] > insidePassLane + 1.5f,
            "Bristol #40 stayed committed to an inside pass with a pulling-away blocker and no room to pass.");
        Debug.Log($"Bristol #40 released its stalled inside pass and moved {traffic.Lanes[0] - insidePassLane:F3} m toward the preferred line.");
    }

    private static void ReleasePassToDraftLine()
    {
        const int approachTicks = 45;
        const float startingDistance = 100f;
        const float subjectSpeed = 45.5f;
        var traffic = new TrafficFixture(-3.553f, BottomLane);
        traffic.Targets[0] = BottomLane;
        traffic.Targets[1] = BottomLane;
        for (int tick = 0; tick < approachTicks; tick++)
        {
            traffic.Place(startingDistance + subjectSpeed * tick * Step, 4.5f - 1.5f * tick * Step, subjectSpeed, 44f);
            traffic.Advance();
        }

        float trainLane = traffic.Lanes[0];
        Require(trainLane - BottomLane > Clearance, "The draft fixture did not establish an outside pass.");
        traffic.DraftLine[0] = true;
        traffic.Targets[0] = trainLane;
        traffic.Advance();
        Require(Mathf.Abs(traffic.Lanes[0] - trainLane) < .00001f, "An old pass moved a car out of its draft lane.");
        Require(traffic.SubjectPassSide == 0, "The old pass was retained in the draft line.");

        traffic.DraftLine[0] = false;
        traffic.Place(startingDistance + subjectSpeed * approachTicks * Step, 7f, subjectSpeed, 44f);
        traffic.Targets[0] = BottomLane;
        traffic.Advance();
        Require(traffic.Lanes[0] < trainLane - .001f, "The old pass persisted after the car joined a draft line.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class TrafficFixture
    {
        private readonly object behavior;
        private readonly MethodInfo avoidTraffic;
        private readonly Array laneControl;
        private readonly FieldInfo passSide;
        private readonly FieldInfo passBlocker;
        private readonly object[] arguments;
        private readonly float[] distances;
        private readonly float[] currentSpeeds;
        private readonly float[] stepSpeeds;
        private readonly float[] startLanes;
        private readonly float[] startLaneSpeeds;
        private bool hasSample;
        internal readonly float[] Targets;
        internal readonly float[] LineAdvance;
        internal readonly bool[] DraftLine;
        internal readonly float[] Lanes;
        internal float SubjectStepSpeed => stepSpeeds[0];
        internal int SubjectPassSide => (int)passSide.GetValue(laneControl.GetValue(0));

        internal TrafficFixture(params float[] lanes)
        {
            int count = lanes.Length;
            Lanes = lanes;
            Targets = new float[count];
            LineAdvance = new float[count];
            DraftLine = new bool[count];
            distances = new float[count];
            currentSpeeds = new float[count];
            stepSpeeds = new float[count];
            startLanes = new float[count];
            startLaneSpeeds = new float[count];
            var stabilize = new bool[count];
            var onTrack = new bool[count];
            var widths = new float[count];
            var insideClearances = new float[count];
            Array.Fill(stabilize, true);
            Array.Fill(onTrack, true);
            Array.Fill(widths, RoadWidth);
            Array.Fill(insideClearances, .75f);
            var assembly = typeof(TrackPreview).Assembly;
            var baseType = assembly.GetType("VehicleBehavior003", true);
            var stateType = baseType.GetNestedType("TrafficLaneControl", BindingFlags.NonPublic);
            Require(stateType != null, "Shared session traffic state was not available to the probe.");
            Type stateArray = stateType.MakeArrayType();
            laneControl = Array.CreateInstance(stateType, count);
            passSide = stateType.GetField("PassSide", InternalInstance);
            passBlocker = stateType.GetField("PassBlocker", InternalInstance);
            Require(passSide != null && passBlocker != null, "Pass commitment was not available to the probe.");
            var behaviorType = assembly.GetType("VehicleBehavior006", true);
            behavior = Activator.CreateInstance(behaviorType, true);
            var sweepingType = behaviorType.BaseType;
            sweepingType.GetProperty("PreferredLineAdvance", InternalInstance).SetValue(behavior, LineAdvance);
            sweepingType.GetProperty("PreferredInsideClearance", InternalInstance).SetValue(behavior, insideClearances);
            avoidTraffic = Array.Find(baseType.GetMethods(InternalInstance), method =>
                method.Name == "AvoidTraffic" && method.GetParameters()[0].ParameterType == stateArray);
            Require(avoidTraffic != null, "Shared traffic controller overload was not available to the probe.");
            arguments = new object[]
            {
                laneControl, stabilize, LapLength, -1f, Step, true,
                distances, widths, onTrack, Lanes, Targets,
                currentSpeeds, stepSpeeds, 8f, DraftLine, true, null, currentSpeeds, startLanes, startLaneSpeeds,
                null, null, false, null
            };
        }

        internal void Place(float distance, float gap, float subjectSpeed, float blockerSpeed)
        {
            PlaceCar(0, distance, subjectSpeed);
            PlaceCar(1, distance + gap, blockerSpeed);
        }

        internal void PlaceCar(int index, float distance, float speed)
        {
            distances[index] = Mathf.Repeat(distance, LapLength);
            currentSpeeds[index] = speed;
        }

        internal void CommitSubjectPass(int blocker, int side)
        {
            object state = laneControl.GetValue(0);
            passBlocker.SetValue(state, blocker);
            passSide.SetValue(state, side);
            laneControl.SetValue(state, 0);
        }

        internal void Advance()
        {
            for (int i = 0; i < Lanes.Length; i++)
            {
                startLaneSpeeds[i] = hasSample ? (Lanes[i] - startLanes[i]) / Step : 0f;
                startLanes[i] = Lanes[i];
            }
            hasSample = true;
            Array.Copy(currentSpeeds, stepSpeeds, currentSpeeds.Length);
            avoidTraffic.Invoke(behavior, arguments);
        }
    }
}
