using UnityEngine;

public sealed partial class TrackPreview
{
    private const float TvShotMeters = 410f;
    private const float TvFrameHeightMeters = 14f;
    private const float DefaultTvFrameScale = 1.15f;
    private const float MinTvFrameScale = .25f;
    private const float MaxTvFrameScale = 2f;
    private float tvFrameScale = DefaultTvFrameScale;
    private float tvFirstStation;
    private float tvCameraSpacing;

    private void ConfigureTvCameras(float finish, float insideSign)
    {
        int count = Mathf.Clamp(Mathf.CeilToInt(lapLength / TvShotMeters), 3, 12);
        tvFirstStation = finish;
        tvCameraSpacing = lapLength / count;
        tvCameraPositions = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float station = finish + i * tvCameraSpacing;
            Vector3 center = TrackPoint(station, 0f, out var heading, out _);
            Vector3 inside = Vector3.Cross(Vector3.up, heading).normalized * insideSign;
            float setback = RoadWidths(station).x * .5f + Mathf.Clamp(tvCameraSpacing * .09f, 10f, 25f);
            float height = Mathf.Clamp(tvCameraSpacing * .07f, 13f, 32f);
            tvCameraPositions[i] = center + inside * setback + Vector3.up * height;
        }
    }

    private Vector3 TvCameraPosition(int subjectIndex)
    {
        float progress = Mathf.Repeat(TvSubjectStation(subjectIndex) - tvFirstStation, lapLength);
        // Track progress chooses the shot even where two parts of the course pass close together.
        // Hold a little beyond each midpoint so small position changes cannot bounce between shots.
        if (activeTvCamera < 0 ||
            Mathf.Abs(Mathf.Repeat(progress - activeTvCamera * tvCameraSpacing + lapLength * .5f, lapLength) -
                lapLength * .5f) > tvCameraSpacing * .58f)
            activeTvCamera = Mathf.FloorToInt(progress / tvCameraSpacing + .5f) % tvCameraPositions.Length;

        return tvCameraPositions[activeTvCamera];
    }

    private float TvSubjectStation(int index)
    {
        if (raceStart != null && !raceReleased)
        {
            var grid = raceStart.GridPosition(index, 0f);
            if (!grid.OnPitRoad)
                return grid.Distance;

            float progress = Mathf.Clamp01(grid.Distance / pitTraffic.RouteLength);
            return pitTraffic.EntryDistance + Mathf.Repeat(pitTraffic.ExitDistance - pitTraffic.EntryDistance, lapLength) * progress;
        }

        var traffic = isQualifying ? qualifying?.Car : trafficCars?[index];
        if (traffic != null)
            return pitTraffic.RaceDistance(traffic);

        return sessionDistances?[index] ?? elapsed * CarSpeed(index) + lapLength * index / cars.Length;
    }

    private static float TvFieldOfView(float distance, float frameScale) =>
        Mathf.Clamp(2f * Mathf.Atan(TvFrameHeightMeters * frameScale * .5f / Mathf.Max(.1f, distance)) *
            Mathf.Rad2Deg, 1f, 75f);
}
