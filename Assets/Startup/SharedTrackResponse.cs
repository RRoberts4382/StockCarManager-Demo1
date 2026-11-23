using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    // The fallback is venue scenery. Authored responder routes and dispatch
    // retain their existing behavior.
    private const float ResponseRoadHalfWidth = 4f;
    private const float ResponsePadHalfWidth = 9f;
    private const float ResponsePadDepth = 14f;

    private void AddSharedTrackResponse()
    {
        if (pitRoute == null || pitRoute.Length < 3 || customLayout == null || iowaResponseUnits.Count > 0)
            return;

        var structures = SharedResponseStructureBounds();
        var pitSamples = SharedResponsePitSamples();
        foreach (int segment in new[] { 0, 2 })
        foreach (float separation in new[] { 12f, 19f, 27f })
        {
            float pitLength = pitLengths[segment];
            if (pitLength < separation + 4f)
                continue;
            // Both connector mouths lie outside the stall-wall caps.
            float fraction = segment == 0 ? 1f - separation / pitLength : separation / pitLength;
            Vector3 center = PitDrivingPosition(pitRoute[segment], fraction, 0f,
                pitHeights[segment], pitHeights[segment + 1]);
            Vector3 tangent = Vector3.ProjectOnPlane(PitTangent(pitRoute[segment], fraction,
                pitHeights[segment], pitHeights[segment + 1]), Vector3.up).normalized;
            Vector3 intoYard = Vector3.Cross(Vector3.up, tangent) * pitWallSide;
            if (intoYard.sqrMagnitude < .5f)
                continue;
            Vector3 mouth = center + intoYard * (definition.pitRoad.widthMeters * .5f - .32f);
            // Pave slightly below the track-side edge, then blend into the yard.
            mouth.y -= .004f;
            foreach (float reach in new[] { 29f, 23f, 17f })
            foreach (float shift in new[] { 0f, 10f, -10f })
            {
                var end = mouth + intoYard * reach + tangent * shift;
                end.y = groundHeight + .029f;
                var road = SharedResponseRoad(mouth, end, intoYard, reach);
                if (!SharedResponseHasClearance(road, intoYard, segment, structures, pitSamples))
                    continue;
                BuildSharedResponseSite(road, intoYard);
                return;
            }
        }

        Debug.LogWarning($"{definition.name}: No clear pit-connector shoulder was found for the emergency vehicle bays.");
    }

    private (Vector3 Left, Vector3 Right)[] SharedResponseRoad(Vector3 start, Vector3 end,
        Vector3 intoYard, float length)
    {
        const int rows = 25;
        var sections = new (Vector3 Left, Vector3 Right)[rows];
        var a = start + intoYard * (length / 3f);
        var b = end - intoYard * (length / 3f);
        a.y = start.y;
        b.y = end.y;
        for (int i = 0; i < rows; i++)
        {
            float t = i / (float)(rows - 1);
            float u = 1f - t;
            Vector3 point = start * (u * u * u) + a * (3f * u * u * t)
                + b * (3f * u * t * t) + end * (t * t * t);
            Vector3 heading = (a - start) * (u * u) + (b - a) * (2f * u * t)
                + (end - b) * (t * t);
            Vector3 lateral = Vector3.Cross(Vector3.up, heading).normalized * ResponseRoadHalfWidth;
            sections[i] = (point - lateral, point + lateral);
        }
        return sections;
    }

    private List<Bounds> SharedResponseStructureBounds()
    {
        var result = new List<Bounds>();
        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            string name = renderer.name;
            bool building = name.Contains("building", StringComparison.OrdinalIgnoreCase)
                || name.Contains("structure", StringComparison.OrdinalIgnoreCase)
                || name.Contains("garage floor", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Michigan garage ", StringComparison.OrdinalIgnoreCase)
                || name.Contains("hauler lot", StringComparison.OrdinalIgnoreCase);
            if (!building || name.Contains("billboard", StringComparison.OrdinalIgnoreCase))
                continue;
            var bounds = renderer.bounds;
            var local = new Bounds(transform.InverseTransformPoint(bounds.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                local.Encapsulate(transform.InverseTransformPoint(bounds.center
                    + new Vector3(bounds.extents.x * x, 0f, bounds.extents.z * z)));
            result.Add(local);
        }
        return result;
    }

    private (Vector3 Position, int Segment)[] SharedResponsePitSamples()
    {
        var points = new List<(Vector3 Position, int Segment)>();
        for (int segment = 0; segment < pitRoute.Length; segment++)
        {
            int samples = Mathf.Max(12, Mathf.CeilToInt(pitLengths[segment] / 3f));
            for (int i = 0; i <= samples; i++)
                points.Add((PitDrivingPosition(pitRoute[segment], i / (float)samples, 0f,
                    pitHeights[segment], pitHeights[segment + 1]), segment));
        }
        return points.ToArray();
    }

    private bool SharedResponseHasClearance((Vector3 Left, Vector3 Right)[] road,
        Vector3 intoYard, int attachedSegment, List<Bounds> structures,
        (Vector3 Position, int Segment)[] pitSamples)
    {
        for (int i = 0; i < road.Length; i += 3)
        {
            var section = road[i];
            if (!Clear(section.Left, i < 9) || !Clear(section.Right, i < 9) ||
                !Clear((section.Left + section.Right) * .5f, i < 9))
                return false;
        }

        var end = (road[^1].Left + road[^1].Right) * .5f;
        var side = Vector3.Cross(Vector3.up, intoYard);
        for (int step = 0; step <= 4; step++)
        for (int column = -1; column <= 1; column++)
            if (!Clear(end + intoYard * (step * ResponsePadDepth / 4f)
                + side * (column * ResponsePadHalfWidth), false))
                return false;
        return true;

        bool Clear(Vector3 point, bool shoulder)
        {
            float closest = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, point);
            Vector3 race = TrackPoint(closest, 0f, out var heading, out _);
            Vector3 across = Vector3.Cross(Vector3.up, heading).normalized;
            float lane = Vector3.Dot(point - race, across) * customLayout.InsideSign;
            Vector2 widths = RoadWidths(closest);
            float halfWidth = widths.x * .5f;
            // Keep the turnout clear of the racing surface and the apron.
            // A connector mouth can meet the apron near its authorized merge.
            float innerLimit = halfWidth + (shoulder ? .3f : widths.y + .8f);
            if (lane > -halfWidth - .8f && lane < innerLimit)
                return false;

            float pitClearance = definition.pitRoad.widthMeters * .5f + .65f;
            foreach (var (position, segment) in pitSamples)
            {
                if (shoulder && segment == attachedSegment)
                    continue;
                float dx = point.x - position.x, dz = point.z - position.z;
                if (dx * dx + dz * dz < pitClearance * pitClearance)
                    return false;
            }
            foreach (var bounds in structures)
                if (point.x > bounds.min.x - .9f && point.x < bounds.max.x + .9f
                    && point.z > bounds.min.z - .9f && point.z < bounds.max.z + .9f)
                    return false;
            return true;
        }
    }

    private void BuildSharedResponseSite((Vector3 Left, Vector3 Right)[] road, Vector3 intoYard)
    {
        var asphalt = MakeMaterial(new Color32(62, 65, 68, 255));
        asphalt.SetFloat("_Smoothness", .09f);
        asphalt.SetTexture("_BaseMap", BristolPavementTexture(false));
        var vertices = new Vector3[road.Length * 2];
        var triangles = new int[(road.Length - 1) * 6];
        for (int i = 0; i < road.Length; i++)
        {
            int index = i * 2;
            vertices[index] = road[i].Left;
            vertices[index + 1] = road[i].Right;
            if (i == road.Length - 1)
                continue;
            int face = i * 6;
            triangles[face] = index;
            triangles[face + 1] = index + 2;
            triangles[face + 2] = index + 1;
            triangles[face + 3] = index + 1;
            triangles[face + 4] = index + 2;
            triangles[face + 5] = index + 3;
        }
        AddRoadMesh("Emergency vehicle access road", asphalt, vertices, triangles);

        Vector3 end = (road[^1].Left + road[^1].Right) * .5f;
        var side = Vector3.Cross(Vector3.up, intoYard);
        var near = end - intoYard * .12f;
        var far = end + intoYard * ResponsePadDepth;
        var pad = side * ResponsePadHalfWidth;
        AddRoadMesh("Emergency vehicle paved bays", asphalt,
            new[] { near - pad, near + pad, far - pad, far + pad },
            new[] { 0, 2, 1, 1, 2, 3 });

        Vector3 center = end + intoYard * 7.1f;
        Quaternion parked = Quaternion.LookRotation(-intoYard, Vector3.up);
        var markings = MakeMaterial(new Color32(225, 226, 212, 255));
        foreach (float offset in new[] { -6.3f, 0f, 6.3f })
        {
            var line = Box("Emergency bay side stripe", transform, center + side * offset + Vector3.up * .014f,
                new Vector3(.09f, .013f, 7.9f), markings);
            line.transform.localRotation = parked;
        }

        iowaResponseVehiclePaint ??= CreateIowaResponseVehiclePaint();
        AddVehicle(center - side * 3.15f, ambulance: true);
        AddVehicle(center + side * 3.15f, ambulance: false);

        void AddVehicle(Vector3 position, bool ambulance)
        {
            var vehicle = CreateObject(ambulance ? "Emergency ambulance" : "Emergency tow truck", transform);
            vehicle.transform.SetLocalPositionAndRotation(position, parked);
            var shape = new IowaPitWorkMesh();
            if (ambulance) BuildIowaAmbulance(shape);
            else BuildIowaTowTruck(shape);
            Mesh mesh = shape.Build(vehicle.name, ambulance ? 0 : 1, 2);
            roadMeshes.Add(mesh);
            vehicle.AddComponent<MeshFilter>().sharedMesh = mesh;
            vehicle.AddComponent<MeshRenderer>().sharedMaterial = iowaResponseVehiclePaint;
            AddIowaResponseWarningLamps(vehicle.transform, ambulance);
        }
    }
}
