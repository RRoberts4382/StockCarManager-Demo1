using System;
using UnityEngine;

public sealed partial class TrackPreview
{
    private bool soloTestSession;
    private Vector2[] testInfieldOutline;

    // These meshes combine individual vehicles or people in the imported venue.
    private static readonly string[] SoloTestSceneryPrefixes =
    {
        "Parked spectator car ", "Camping motorhome ", "Camper golf cart ", "Camper barbecue grill",
        "Michigan infield access parking - occupied stalls", "Michigan protected pit promenade spectators",
        "Michigan hauler display ",
        "Phoenix medical and west parking parked vehicles", "Phoenix infield main paddock parking parked vehicles",
        "Phoenix east garage lot parked vehicles", "Phoenix exterior spectator camp lot 3 parked vehicles",
        "Richmond infield campground RVs golf carts and grills", "Richmond media parking parked vehicles",
        "Richmond paddock parking parked vehicles", "Richmond support parking behind haulers parked vehicles",
        "Richmond backstretch RV camping parked vehicles", "Richmond turn 3 RV camping parked vehicles",
        "North Wilkesboro infield staff parking parked vehicles"
    };

    private static bool HideSoloTestMesh(string name)
    {
        foreach (string prefix in SoloTestSceneryPrefixes)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;

        return name.Equals("North Wilkesboro outside backstretch RV camping", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Michigan RV site ", StringComparison.OrdinalIgnoreCase) &&
                name.Contains(" parked campers", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Phoenix exterior RV camp lot ", StringComparison.OrdinalIgnoreCase) &&
                name.Contains(" parked vehicles", StringComparison.OrdinalIgnoreCase);
    }

    private void HideSoloTestScenery()
    {
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
            if (HideSoloTestMesh(renderer.name)) renderer.enabled = false;
    }

    private bool IsInfieldPosition(Vector3 position)
    {
        if (testInfieldOutline == null)
        {
            const int samples = 256;
            testInfieldOutline = new Vector2[samples];
            for (int i = 0; i < samples; i++)
            {
                var point = TrackPoint(lapLength * i / samples, 0f, out _, out _);
                testInfieldOutline[i] = new Vector2(point.x, point.z);
            }
        }

        // Crossing the course's centerline changes whether the point is infield.
        bool inside = false;
        Vector2 previous = testInfieldOutline[^1];
        foreach (var point in testInfieldOutline)
        {
            if ((point.y > position.z) != (previous.y > position.z) &&
                position.x < previous.x + (point.x - previous.x) * (position.z - previous.y) / (point.y - previous.y))
                inside = !inside;
            previous = point;
        }
        return inside;
    }
}
