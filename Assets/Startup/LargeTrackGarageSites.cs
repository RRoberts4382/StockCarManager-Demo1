using UnityEngine;

public sealed partial class TrackPreview
{
    // The site and mouth positions were fitted against the sampled Unity track and pit geometry.
    private static readonly (int Id, float Anchor, float Scale, float Front, float Depth,
        float HalfThroat, float Entry, float Exit)[] LargeGarageSites =
    {
        // Garage placement rows
        (4, 0.32f, 1f, 20f, 48f, 3.65f, 0.14129035f, -1f),
        (7, 0.81f, 1f, 28f, 48f, 5.45f, 0.66767262f, 0.95729912f),
        (9, 0.5f, 1f, 28f, 48f, 4f, 0.03761941f, 0.9730728f),
        (10, 0.845f, 1f, 19f, 48f, 5.45f, 0.71137431f, 0.97576565f),
        (11, 0.88f, 1f, 19f, 48f, 3.65f, 0.78957834f, 0.97374401f),
        (12, 0.215f, 1f, 21.144f, 48f, 4f, 0.0418454f, 0.37762394f),
        (13, 0.5f, 1f, 12f, 48f, 4f, 0.02513374f, 0.97486626f),
        (14, 0.5f, 1f, 40f, 48f, 5.45f, 0.03484344f, 0.96999652f),
        (16, 0.18f, 1f, 28f, 48f, 5.45f, 0.03180966f, 0.33311075f),
        (17, 0.5f, 1f, 12f, 48f, 4f, 0.0268593f, 0.9731407f),
        (18, 0.18f, 1f, 19f, 48f, 3.65f, 0.05484661f, 0.27754702f),
        (19, 0.355f, 1f, 19f, 48f, 5.45f, 0.27800598f, 0.43310275f),
        (20, 0.355f, 1f, 19f, 48f, 4.55f, 0.27708326f, 0.43386121f),
        (22, 0.845f, 1f, 19f, 48f, 5.45f, 0.71137519f, 0.97757488f),
        (23, 0.88f, 1f, 12.096f, 42f, 4.55f, 0.97576892f, -1f),
        (24, 0.845f, 1f, 19f, 48f, 5.45f, 0.7069839f, 0.97125146f),
        (26, 0.845f, 1f, 28f, 48f, 5.45f, 0.71137718f, 0.98013652f),
        (30, 0.39f, 1f, 19f, 42f, 4.55f, 0.26744191f, 0.50697842f),
        (34, 0.495f, 1f, 19f, 48f, 3.65f, 0.34191662f, 0.64069946f),
    };

    private bool TryAddLargeTrackGarage(TrackPitSpline pit, float width, float roadStart, float roadEnd)
    {
        var site = System.Array.Find(LargeGarageSites, item => item.Id == definition.id);
        if (site.Id == 0)
            return false;

        float pitLength = pitLengths[1];
        float halfOpening = (site.HalfThroat + 0.85f) / pitLength;
        const float defaultWallEnd = 0.965f;
        float wallStart = 0.035f;
        SplitWallAt(site.Entry);
        if (site.Exit >= 0f)
            SplitWallAt(site.Exit);
        if (wallStart < defaultWallEnd)
            DrawPitWall(pit, width, roadStart, roadEnd, startFraction: wallStart);

        var origin = PitPosition(pit, site.Anchor, roadStart, roadEnd);
        var infield = Vector3.Cross(Vector3.up, PitTangent(pit, site.Anchor, roadStart, roadEnd)).normalized * pitWallSide;
        var rotation = Quaternion.LookRotation(infield, Vector3.up);
        var right = rotation * Vector3.right;
        float halfCourt = 55f * site.Scale + 14f;
        float centerDepth = site.Front + site.Depth * 0.5f;
        string venue = definition.name;

        var asphalt = MakeMaterial(new Color32(65, 68, 72, 255));
        asphalt.SetFloat("_Smoothness", 0.07f);
        asphalt.SetTexture("_BaseMap", BristolPavementTexture(false));
        var concrete = MakeMaterial(new Color32(146, 143, 135, 255));
        concrete.SetTexture("_BaseMap", BristolPavementTexture(true));

        AddGaragePlane(venue + " garage motor court", asphalt, halfCourt, site.Front, site.Front + site.Depth, 0f);
        AddGaragePlane(venue + " garage concrete threshold", concrete, 55f * site.Scale,
            centerDepth - 9.2f, centerDepth - 6.9f, 0.006f);
        AddGaragePlane(venue + " garage rear concrete threshold", concrete, 55f * site.Scale,
            centerDepth + 6.9f, centerDepth + 9.2f, 0.006f);

        AddAccess(venue + (site.Exit >= 0f ? " garage entry road" : " garage access road"), site.Entry);
        if (site.Exit >= 0f)
            AddAccess(venue + " garage exit road", site.Exit);

        var garage = CreateObject(venue + " infield garage", transform).transform;
        garage.localPosition = At(0f, centerDepth);
        garage.localRotation = rotation;
        garage.localScale = new Vector3(site.Scale, 1f, 1f);
        AddInfieldGarageBuilding(garage, style: 1 + definition.id % 6);
        return true;

        Vector3 At(float x, float z, float y = 0f) => origin + rotation * new Vector3(x, y, z);

        void SplitWallAt(float fraction)
        {
            float end = Mathf.Min(defaultWallEnd, fraction - halfOpening);
            if (end > wallStart + 0.0001f)
                DrawPitWall(pit, width, roadStart, roadEnd, wallStart, end);
            wallStart = Mathf.Max(wallStart, fraction + halfOpening);
        }

        void AddGaragePlane(string name, Material material, float halfWidth, float near, float far, float height)
        {
            AddRoadMesh(name, material,
                new[] { At(-halfWidth, near, height), At(halfWidth, near, height),
                    At(-halfWidth, far, height), At(halfWidth, far, height) },
                new[] { 0, 2, 1, 1, 2, 3 });
        }

        Vector3 PitEdge(float fraction)
        {
            // Interpolate the actual boundary of DrawPitRibbon, including on a curved pit road.
            float row = Mathf.Clamp01(fraction) * PitRibbonSegments;
            int sample = Mathf.Min((int)row, PitRibbonSegments - 1);
            return Vector3.Lerp(AtRow(sample), AtRow(sample + 1), row - sample);

            Vector3 AtRow(int index)
            {
                float f = index / (float)PitRibbonSegments;
                var normal = Vector3.Cross(Vector3.up, PitTangent(pit, f, roadStart, roadEnd)).normalized;
                return PitPosition(pit, f, roadStart, roadEnd) + normal * (pitWallSide * width * 0.5f);
            }
        }

        void AddAccess(string name, float station)
        {
            const int sections = 32;
            float half = site.HalfThroat / pitLength;
            var startLeft = PitEdge(station - half);
            var startRight = PitEdge(station + half);
            var forward = Vector3.Cross(Vector3.up, PitTangent(pit, station, roadStart, roadEnd)).normalized * pitWallSide;
            if (Vector3.Dot(startRight - startLeft, Vector3.Cross(Vector3.up, forward)) < 0f)
                (startLeft, startRight) = (startRight, startLeft);

            var mouth = (startLeft + startRight) * 0.5f;
            const float straightThroat = 2.85f;
            var start = mouth + forward * straightThroat;
            float maxX = halfCourt - site.HalfThroat - 1f;
            float x = Vector3.Dot(start - origin, right);
            bool enterFromSide = Mathf.Abs(x) > halfCourt + 1f;
            var end = enterFromSide
                ? At(Mathf.Sign(x) * halfCourt, site.Front + site.HalfThroat + 5f)
                : At(Mathf.Clamp(x, -maxX, maxX), site.Front);
            var endDirection = enterFromSide ? -Mathf.Sign(x) * right : infield;
            float bend = Mathf.Min(13f, Vector3.Distance(start, end) * 0.39f);
            var first = start + forward * bend;
            var last = end - endDirection * bend;
            const int jointSections = 2;
            var vertices = new Vector3[(sections + jointSections + 1) * 2];
            var triangles = new int[(sections + jointSections) * 6];
            // Tuck a short strip below the existing surface so curved seams stay closed.
            var underlap = -forward * 0.24f - Vector3.up * 0.003f;
            vertices[0] = startLeft + underlap;
            vertices[1] = startRight + underlap;
            vertices[2] = startLeft;
            vertices[3] = startRight;
            vertices[4] = startLeft + forward * straightThroat;
            vertices[5] = startRight + forward * straightThroat;
            for (int i = 1; i <= sections; i++)
            {
                float t = i / (float)sections;
                float u = 1f - t;
                var center = (u * u * u) * start + (3f * u * u * t) * first +
                    (3f * u * t * t) * last + (t * t * t) * end;
                var tangent = (u * u) * (first - start) + (2f * u * t) * (last - first) +
                    (t * t) * (end - last);
                var across = Vector3.Cross(Vector3.up, tangent).normalized * site.HalfThroat;
                int vertex = (i + jointSections) * 2;
                vertices[vertex] = center - across;
                vertices[vertex + 1] = center + across;
            }
            for (int i = 0; i < sections + jointSections; i++)
            {
                int a = i * 2;
                int triangle = i * 6;
                triangles[triangle] = a;
                triangles[triangle + 1] = a + 2;
                triangles[triangle + 2] = a + 1;
                triangles[triangle + 3] = a + 1;
                triangles[triangle + 4] = a + 2;
                triangles[triangle + 5] = a + 3;
            }
            AddRoadMesh(name, asphalt, vertices, triangles);
        }
    }
}
