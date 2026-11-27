using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class TrackPreview
{
    private const float MarbleDepositRate = .04f;
    private const float MinimumMarbleShoulder = .35f;
    private const float MarblePavementInset = .08f;
    private float[] marbleDensity;
    private float[] marbleSlipTime;
    // Each sample describes the groove center, pavement half width, clean groove half width, and turn weight.
    private Vector4[] marbleCrossSections;
    private float marbleOutsideSign;
    private float marbleUploadAt;
    private bool marbleDensityDirty;
    private Texture2D marbleDensityMap;
    private Color32[] marblePixels;
    private Mesh marbleOverlayMesh;
    private MeshRenderer marbleOverlayRenderer;

    internal bool SupportsMarbles => marbleDensity != null;
    internal bool MarblesEnabled { get; private set; } = true;

    internal void SetMarblesEnabled(bool enabled)
    {
        if (MarblesEnabled == enabled)
            return;

        MarblesEnabled = enabled;
        if (marbleSlipTime != null)
            System.Array.Clear(marbleSlipTime, 0, marbleSlipTime.Length);
        if (marbleOverlayRenderer == null)
            return;

        if (!enabled)
        {
            marbleOverlayRenderer.enabled = false;
            return;
        }

        // Retain rubber shed while the effect was off for a direct comparison.
        marbleDensityDirty = true;
        marbleUploadAt = 0f;
        PresentMarbleDensity();
    }

    private void BuildTrackMarbles(RaceSession session)
    {
        marbleDensity = null;
        marbleSlipTime = null;
        marbleDensityDirty = false;
        if (marbleOverlayRenderer != null)
            marbleOverlayRenderer.enabled = false;
        if (session.kind != RaceSessionKind.Race || definition.majorStyle != "Oval" ||
            racingLine == null || lapLength <= 0f)
            return;

        int bins = Mathf.Clamp(Mathf.CeilToInt(lapLength / 8f), 64, 1024);
        marbleDensity = new float[bins];
        marbleSlipTime = new float[cars.Length];
        marbleCrossSections = new Vector4[bins];
        marbleOutsideSign = -(customLayout?.InsideSign ?? -1f);
        marbleUploadAt = 0f;
        SessionRun.TireRubberShed = DepositMarbles;

        for (int i = 0; i < bins; i++)
        {
            float distance = i * lapLength / bins;
            TrackPoint(distance - 12f, 0f, out var before, out _);
            TrackPoint(distance + 12f, 0f, out var after, out _);
            float turn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, 16f, Vector3.Angle(before, after)));
            float halfWidth = Mathf.Max(0f, RoadWidths(distance).x * .5f - MarblePavementInset);
            float center = Mathf.Clamp(PreferredLaneAt(0, distance), -halfWidth, halfWidth);
            float groove = Mathf.Min(halfWidth * .65f, Mathf.Lerp(1.3f, 1.8f, turn));
            marbleCrossSections[i] = new Vector4(center, halfWidth, groove, turn);
        }

        BuildMarbleOverlay();
    }

    private void MarbleSample(float distance, out int index, out int next, out float fraction)
    {
        float sample = Mathf.Repeat(distance, lapLength) / lapLength * marbleDensity.Length;
        index = Mathf.Min((int)sample, marbleDensity.Length - 1);
        next = (index + 1) % marbleDensity.Length;
        fraction = sample - index;
    }

    private void DepositMarbles(float distance, float wear)
    {
        MarbleSample(distance, out int index, out int next, out float fraction);
        // Normalize by the distance covered by a bin so larger venues need no special tuning.
        float wearPerMile = wear * TrackDefinition.MetersPerMile * marbleDensity.Length / lapLength;
        float turnWeight = Mathf.Lerp(marbleCrossSections[index].w, marbleCrossSections[next].w, fraction);
        float deposit = wearPerMile * MarbleDepositRate * Mathf.Lerp(.28f, 1f, turnWeight);
        marbleDensity[index] = Mathf.Min(1f, marbleDensity[index] + deposit * (1f - fraction));
        marbleDensity[next] = Mathf.Min(1f, marbleDensity[next] + deposit * fraction);
        marbleDensityDirty = true;
    }

    private float MarbleLoadAt(int index, float distance, float lane)
    {
        if (!MarblesEnabled || marbleDensity == null)
            return 0f;

        MarbleSample(distance, out int first, out int next, out float fraction);
        float density = Mathf.Lerp(marbleDensity[first], marbleDensity[next], fraction);
        if (density <= 0f)
            return 0f;

        Vector4 section = Vector4.Lerp(marbleCrossSections[first], marbleCrossSections[next], fraction);
        float bandStart = MarbleBandStart(section, section.y);
        if (bandStart >= section.y || Mathf.Abs(lane) > section.y + .15f)
            return 0f;

        // Evaluate the outside tires against the same wallward gradient used by the overlay.
        float halfWidth = VehicleCollision001.TrackContactWidth(raceCarHalfWidths, index, index) * .5f;
        float outsideTire = lane * marbleOutsideSign + .8f * halfWidth;
        float towardWall = Mathf.InverseLerp(bandStart, section.y, outsideTire);
        return density * towardWall * towardWall;
    }

    private float MarbleBandStart(Vector4 section, float halfWidth) =>
        Mathf.Clamp(section.x * marbleOutsideSign + section.z, -halfWidth,
            Mathf.Max(-halfWidth, halfWidth - MinimumMarbleShoulder));

    private float MarbleLoadForCar(int index, float distance)
    {
        if (!MarblesEnabled || marbleDensity == null || !drivingLanesReady || !CanApplyTireHandling(index, trafficCars?[index]) ||
            trafficCars?[index]?.InPit == true || stages?.ReducedFuelBurn == true)
            return 0f;

        return MarbleLoadAt(index, distance, drivingLanes[index]);
    }

    private static float MarbleGrip(float load) => 1f - .42f * load;

    private void TryMarbleSpin(int index, float seconds)
    {
        if (!MarblesEnabled || marbleDensity == null)
            return;

        float distance = collisionEndDistances[index];
        float load = collisionParticipants[index] ? MarbleLoadForCar(index, distance) : 0f;
        float turn = Mathf.Abs(SessionRun.TireHandlingTurnAt(distance));
        if (load >= .48f && turn >= .42f)
        {
            float speed = SessionRun.CurrentSpeed(index);
            float cleanSpeed = SetupAdjustedSpeedAt(index, distance, 0f);
            if (speed >= Mathf.Max(18f, cleanSpeed * Mathf.Sqrt(MarbleGrip(load))))
                marbleSlipTime[index] += seconds * Mathf.Lerp(.65f, 1.5f, load);
            else
                marbleSlipTime[index] = Mathf.MoveTowards(marbleSlipTime[index], 0f, seconds * 2f);
        }
        else
            marbleSlipTime[index] = Mathf.MoveTowards(marbleSlipTime[index], 0f, seconds * 2f);

        if (marbleSlipTime[index] < .45f)
            return;

        marbleSlipTime[index] = 0f;
        StartSpin(index, marbleOutsideSign * Mathf.Lerp(3.5f, 6f, load));
        crashes[index].Cause = CrashCause.Marbles;
    }

    private void BuildMarbleOverlay()
    {
        var shader = Resources.Load<Shader>("Startup/TrackMarbles");
        if (shader == null || !shader.isSupported)
            return;

        if (marbleOverlayMesh == null)
        {
            const string label = "Track marbles";
            marbleOverlayMesh = new Mesh { name = label, indexFormat = IndexFormat.UInt32 };
            roadMeshes.Add(marbleOverlayMesh);
            var overlay = CreateObject(label, transform);
            overlay.AddComponent<MeshFilter>().sharedMesh = marbleOverlayMesh;
            marbleOverlayRenderer = overlay.AddComponent<MeshRenderer>();
            marbleOverlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            marbleOverlayRenderer.receiveShadows = false;
            var material = new Material(shader);
            materials.Add(material);
            marbleOverlayRenderer.sharedMaterial = material;
        }

        if (marbleDensityMap == null || marbleDensityMap.width != marbleDensity.Length)
        {
            marbleDensityMap = new Texture2D(marbleDensity.Length, 1, TextureFormat.RGBA32, false, true)
            {
                name = "Marble density around the lap",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            carPaintTextures.Add(marbleDensityMap);
            marbleOverlayRenderer.sharedMaterial.SetTexture("_Density", marbleDensityMap);
        }

        int samples = Mathf.Clamp(Mathf.CeilToInt(lapLength / 2f), 128, 4096);
        var vertices = new List<Vector3>(2 * (samples + 1));
        var coordinates = new List<Vector2>(vertices.Capacity);
        var triangles = new List<int>(6 * samples);
        for (int row = 0; row <= samples; row++)
        {
            float station = (row % samples) * lapLength / samples;
            MarbleSample(station, out int index, out int next, out float fraction);
            Vector4 section = Vector4.Lerp(marbleCrossSections[index], marbleCrossSections[next], fraction);
            float halfWidth = Mathf.Max(0f, RoadWidths(station).x * .5f - MarblePavementInset);
            float textureStation = (float)row / samples + .5f / marbleDensity.Length;
            float start = MarbleBandStart(section, halfWidth);
            int firstVertex = vertices.Count;
            AddVertex(start * marbleOutsideSign, 0f);
            AddVertex(halfWidth * marbleOutsideSign, 1f);
            if (row > 0)
                JoinRubberRows(triangles, firstVertex, 2, 2);

            void AddVertex(float lane, float towardWall)
            {
                Vector3 point = TrackPoint(station, lane, out _, out var up);
                vertices.Add(point + up * .067f);
                coordinates.Add(new Vector2(textureStation, towardWall));
            }
        }

        marbleOverlayMesh.Clear();
        marbleOverlayMesh.SetVertices(vertices);
        marbleOverlayMesh.SetUVs(0, coordinates);
        marbleOverlayMesh.SetTriangles(triangles, 0);
        marblePixels = new Color32[marbleDensity.Length];
        marbleDensityDirty = true;
        PresentMarbleDensity();
    }

    private void PresentMarbleDensity()
    {
        if (!MarblesEnabled || !marbleDensityDirty || marbleDensityMap == null ||
            Time.unscaledTime < marbleUploadAt)
            return;

        bool visible = false;
        for (int i = 0; i < marblePixels.Length; i++)
        {
            byte density = (byte)Mathf.RoundToInt(marbleDensity[i] * 255f);
            marblePixels[i] = new Color32(density, 0, 0, 255);
            visible |= density > 0;
        }
        marbleDensityMap.SetPixels32(marblePixels);
        marbleDensityMap.Apply(false);
        marbleOverlayRenderer.enabled = visible;
        marbleDensityDirty = false;
        marbleUploadAt = Time.unscaledTime + .4f;
    }
}
