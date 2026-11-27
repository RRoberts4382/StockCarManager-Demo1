using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class TrackPreview
{
    private const float IowaFanAnimationInterval = 1f / 15f;
    private List<IowaFanRequest> iowaFanRequests;
    private List<IowaFanBatch> iowaWalkingFans;
    private float iowaFanElapsed;

    private void QueueIowaStandingFan(Vector3 position, Vector3 facing, int seed, int teamIndex = -1)
    {
        if (soloTestSession && IsInfieldPosition(position))
            return;
        facing.y = 0f;
        if (facing.sqrMagnitude < .001f)
            facing = Vector3.forward;
        iowaFanRequests ??= new List<IowaFanRequest>();
        iowaFanRequests.Add(new IowaFanRequest(position, position, facing, seed, teamIndex, false));
    }

    private void QueueIowaWalkingFan(Vector3 start, Vector3 end, int seed, int teamIndex = -1,
        float[] groundHeights = null)
    {
        if (soloTestSession)
        {
            int segments = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / 2f));
            for (int i = 0; i <= segments; i++)
                if (IsInfieldPosition(Vector3.Lerp(start, end, i / (float)segments))) return;
        }
        Vector3 facing = end - start;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1f)
        {
            QueueIowaStandingFan(start, facing, seed, teamIndex);
            return;
        }
        iowaFanRequests ??= new List<IowaFanRequest>();
        iowaFanRequests.Add(new IowaFanRequest(start, end, facing, seed, teamIndex, true, groundHeights));
    }

    private void BuildIowaFans()
    {
        if (iowaFanRequests == null || iowaFanRequests.Count == 0)
            return;

        const float tileSize = 55f;
        var palette = new IowaFanPalette();
        var tiles = new Dictionary<(int X, int Z, bool Walking), IowaFanBatch>();
        int walkingTileCount = 0;
        foreach (var fan in iowaFanRequests)
        {
            Vector3 middle = (fan.Start + fan.End) * .5f;
            var key = (Mathf.FloorToInt(middle.x / tileSize), Mathf.FloorToInt(middle.z / tileSize), fan.Walking);
            if (!tiles.TryGetValue(key, out var batch))
            {
                batch = new IowaFanBatch(palette, fan.Walking);
                tiles.Add(key, batch);
                if (fan.Walking)
                    walkingTileCount++;
            }

            var random = new System.Random(fan.Seed);
            int team = fan.Team >= 0 ? fan.Team : random.NextDouble() < .57 ? random.Next(renderedCarPaint.Length) : -1;
            Color32 shirt, trim;
            if (team >= 0 && team < renderedCarPaint.Length && renderedCarPaint[team].RoofAndSides.a > 0)
            {
                shirt = renderedCarPaint[team].RoofAndSides;
                trim = renderedCarPaint[team].HoodAndTrunk;
            }
            else
            {
                shirt = IowaFanBatch.CasualShirts[random.Next(IowaFanBatch.CasualShirts.Length)];
                trim = IowaFanBatch.CasualShirts[random.Next(IowaFanBatch.CasualShirts.Length)];
            }
            batch.Add(fan, random, shirt, trim);
        }
        iowaFanRequests.Clear();

        Texture2D texture = palette.CreateTexture();
        carPaintTextures.Add(texture);
        Material paint = MakeMaterial(Color.white);
        paint.name = "Iowa spectators and pedestrians";
        paint.SetTexture("_BaseMap", texture);
        paint.SetFloat("_Smoothness", .10f);
        var parent = CreateObject("Iowa fans", transform).transform;

        foreach (var pair in tiles)
        {
            string group = pair.Key.Walking ? "Iowa walking fans" : "Iowa standing fans";
            string name = $"{group} {pair.Key.X}, {pair.Key.Z}";
            Mesh mesh = pair.Value.Build(name);
            roadMeshes.Add(mesh);
            var item = CreateObject(name, parent);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = paint;
            renderer.renderingLayerMask = BristolLightingLayer;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (pair.Key.Walking)
            {
                iowaWalkingFans ??= new List<IowaFanBatch>();
                // Spread the uploads across display frames when several areas are visible.
                pair.Value.NextUpdate = iowaFanElapsed + IowaFanAnimationInterval *
                    (iowaWalkingFans.Count + .5f) / walkingTileCount;
                iowaWalkingFans.Add(pair.Value);
            }
        }
    }

    private void AnimateIowaFans(float seconds)
    {
        if (iowaWalkingFans == null || seconds <= 0f)
            return;

        iowaFanElapsed += seconds;
        foreach (var batch in iowaWalkingFans)
        {
            if (iowaFanElapsed < batch.NextUpdate)
                continue;

            batch.Update(iowaFanElapsed);
            batch.NextUpdate += IowaFanAnimationInterval *
                (Mathf.Floor((iowaFanElapsed - batch.NextUpdate) / IowaFanAnimationInterval) + 1f);
        }
    }

    private readonly struct IowaFanRequest
    {
        internal readonly Vector3 Start, End, Facing;
        internal readonly int Seed, Team;
        internal readonly bool Walking;
        internal readonly float[] GroundHeights;

        internal IowaFanRequest(Vector3 start, Vector3 end, Vector3 facing, int seed, int team, bool walking,
            float[] groundHeights = null)
        {
            Start = start;
            End = end;
            Facing = facing;
            Seed = seed;
            Team = team;
            Walking = walking;
            GroundHeights = groundHeights;
        }
    }

    private sealed class IowaFanPalette
    {
        private const int Width = 64;
        private int height;
        private readonly List<Color32> swatches = new List<Color32>();
        private readonly Dictionary<Color32, int> lookup = new Dictionary<Color32, int>();

        internal int Add(Color32 color)
        {
            color.a = 255;
            if (lookup.TryGetValue(color, out int index))
                return index;
            index = swatches.Count;
            lookup.Add(color, index);
            swatches.Add(color);
            return index;
        }

        internal Texture2D CreateTexture()
        {
            height = Mathf.Max(1, (swatches.Count + Width - 1) / Width);
            var texels = new Color32[Width * height];
            swatches.CopyTo(texels);
            var texture = new Texture2D(Width, height, TextureFormat.RGBA32, false)
            {
                name = "Iowa fan shirt and skin palette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(texels);
            texture.Apply(false, true);
            return texture;
        }

        internal Vector2 Coordinate(int index) =>
            new Vector2((index % Width + .5f) / Width, (index / Width + .5f) / height);
    }

    private sealed class IowaFanBatch
    {
        internal static readonly Color32[] CasualShirts =
        {
            new Color32(195, 191, 177, 255), new Color32(74, 105, 122, 255), new Color32(146, 60, 64, 255),
            new Color32(73, 100, 74, 255), new Color32(49, 55, 66, 255), new Color32(210, 156, 71, 255),
            new Color32(133, 103, 137, 255), new Color32(69, 132, 146, 255), new Color32(230, 214, 173, 255),
            new Color32(154, 103, 76, 255), new Color32(39, 83, 98, 255), new Color32(166, 171, 181, 255)
        };

        private static readonly Color32[] SkinTones =
        {
            new Color32(246, 202, 174, 255), new Color32(225, 173, 139, 255), new Color32(204, 147, 111, 255),
            new Color32(171, 112, 85, 255), new Color32(141, 90, 66, 255), new Color32(103, 67, 50, 255),
            new Color32(76, 51, 41, 255)
        };

        private static readonly Color32[] Pants =
        {
            new Color32(43, 55, 75, 255), new Color32(67, 83, 102, 255), new Color32(106, 122, 135, 255),
            new Color32(52, 50, 49, 255), new Color32(136, 114, 90, 255), new Color32(176, 164, 137, 255),
            new Color32(71, 81, 66, 255), new Color32(100, 83, 75, 255)
        };

        private static readonly Color32[] Hair =
        {
            new Color32(32, 27, 26, 255), new Color32(67, 46, 39, 255), new Color32(100, 73, 54, 255),
            new Color32(142, 96, 55, 255), new Color32(183, 152, 95, 255), new Color32(98, 98, 94, 255),
            new Color32(213, 211, 199, 255)
        };

        private enum Bone : byte { Body, LeftLeg, RightLeg, LeftArm, RightArm }

        private readonly IowaFanPalette palette;
        private readonly bool walking;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<int> colors = new List<int>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Bone> bones = new List<Bone>();
        private readonly List<Walker> walkers = new List<Walker>();
        private Mesh mesh;
        private Vector3[] baseVertices, baseNormals, currentVertices, currentNormals;
        private Bounds travelBounds;
        internal float NextUpdate { get; set; }

        internal IowaFanBatch(IowaFanPalette palette, bool walking)
        {
            this.palette = palette;
            this.walking = walking;
        }

        internal void Add(IowaFanRequest fan, System.Random random, Color32 shirt, Color32 trim)
        {
            float age = (float)random.NextDouble();
            bool child = age < .13f;
            bool teen = age >= .13f && age < .28f;
            bool older = age >= .80f;
            float h = child ? Range(.88f, 1.28f) : teen ? Range(1.30f, 1.69f) : older ? Range(1.48f, 1.82f) : Range(1.54f, 1.95f);
            float frame = Range(.84f, 1.22f);
            float shoulders = h * .112f * frame;
            float hip = h * (child ? .045f : .049f) * frame;
            float shoulderY = h * (older ? .738f : .755f);
            float hipY = h * (child ? .452f : .472f);
            float headRadius = h * (child ? .090f : teen ? .077f : .068f);
            int skin = palette.Add(SkinTones[random.Next(SkinTones.Length)]);
            int jersey = palette.Add(shirt);
            int seam = palette.Add(trim);
            int trousers = palette.Add(Pants[random.Next(Pants.Length)]);
            int shoes = palette.Add(random.Next(3) == 0 ? new Color32(205, 199, 187, 255) : new Color32(43, 44, 47, 255));
            int leather = palette.Add(Hair[older && random.Next(5) != 0 ? random.Next(5, 7) : random.Next(5)]);
            int visor = palette.Add(new Color32(43, 51, 56, 255));
            bool shorts = random.NextDouble() < (older ? .15 : .36);
            bool hat = random.NextDouble() < .39;
            int pose = walking ? 0 : random.Next(5);
            int start = vertices.Count;

            Pipe(new Vector3(0f, hipY * .94f, 0f), new Vector3(0f, hipY * 1.10f, 0f),
                h * .091f * frame, h * .089f * frame, trousers, Bone.Body, .66f);
            Pipe(new Vector3(0f, hipY * 1.01f, 0f), new Vector3(0f, shoulderY + h * .021f, 0f),
                h * .087f * frame, shoulders * 1.02f, jersey, Bone.Body, .64f);
            if (random.NextDouble() < .62)
                Box(new Vector3(0f, shoulderY - h * .056f, shoulders * .624f),
                    new Vector3(shoulders * 1.57f, h * .023f, h * .013f), seam, Bone.Body);

            Vector3 head = new Vector3(0f, h - headRadius - .011f, older ? h * .016f : 0f);
            Vector3 headSize = new Vector3(headRadius * .79f, headRadius, headRadius * .76f);
            Pipe(new Vector3(0f, shoulderY, 0f), new Vector3(0f, head.y - headRadius * .56f, head.z),
                headRadius * .48f, headRadius * .45f, skin, Bone.Body);
            Globe(head, headSize, skin, Bone.Body);
            Globe(head + new Vector3(0f, headRadius * .34f, -headRadius * .24f),
                new Vector3(headSize.x * 1.04f, headRadius * .66f, headSize.z * .95f), leather, Bone.Body);
            if (hat)
            {
                Globe(head + Vector3.up * (headRadius * .49f), new Vector3(headSize.x * 1.11f, headRadius * .57f, headSize.z * 1.14f),
                    random.Next(2) == 0 ? jersey : seam, Bone.Body);
                Box(head + new Vector3(0f, headRadius * .47f, headSize.z * .98f),
                    new Vector3(headSize.x * 1.85f, headRadius * .16f, headSize.z * .89f), seam, Bone.Body);
            }
            else if (older || random.Next(7) == 0)
                Box(head + new Vector3(0f, headRadius * .15f, headSize.z * .96f),
                    new Vector3(headSize.x * 1.47f, headRadius * .18f, headSize.z * .15f), visor, Bone.Body);

            for (int side = -1; side <= 1; side += 2)
            {
                Bone arm = side < 0 ? Bone.LeftArm : Bone.RightArm;
                Vector3 shoulder = new Vector3(side * shoulders, shoulderY, 0f);
                Vector3 elbow = new Vector3(side * (shoulders + h * .021f), shoulderY - h * .123f,
                    walking ? h * .019f : 0f);
                Vector3 hand = new Vector3(side * (shoulders + h * .014f), shoulderY - h * .231f, h * .027f);
                if (!walking && side < 0 && pose == 1)
                {
                    elbow.x += side * h * .037f;
                    hand = new Vector3(elbow.x + side * h * .008f, shoulderY + h * .037f, h * .016f);
                }
                else if (!walking && pose == 2)
                {
                    elbow.x += side * h * .044f;
                    hand = new Vector3(side * shoulders * .70f, h * .515f, h * .055f);
                }
                else if (!walking && pose == 3)
                    hand = new Vector3(side * shoulders * .71f, h * .557f, h * .086f);
                Vector3 sleeveEnd = Vector3.Lerp(shoulder, elbow, .62f);
                Pipe(shoulder, sleeveEnd, h * .040f, h * .031f, jersey, arm);
                Pipe(sleeveEnd, elbow, h * .026f, h * .022f, skin, arm);
                Pipe(elbow, hand, h * .022f, h * .014f, skin, arm);
                Globe(hand, new Vector3(h * .020f, h * .029f, h * .017f), skin, arm);

                Bone leg = side < 0 ? Bone.LeftLeg : Bone.RightLeg;
                float shift = !walking && pose == 4 ? side * h * .036f : 0f;
                Vector3 thigh = new Vector3(side * hip, hipY, 0f);
                Vector3 knee = new Vector3(side * hip * 1.09f, h * .265f, shift + h * .012f);
                Vector3 ankle = new Vector3(side * hip * 1.16f, h * .050f, shift);
                Pipe(thigh, knee, h * .044f * frame, h * .032f, trousers, leg);
                Pipe(knee, ankle, h * .030f, h * .020f, shorts ? skin : trousers, leg);
                Box(new Vector3(ankle.x, h * .027f, ankle.z + h * .036f),
                    new Vector3(h * .056f, h * .050f, h * .132f), shoes, leg);
            }

            Quaternion facing = Quaternion.LookRotation(fan.Facing, Vector3.up);
            if (!walking)
            {
                for (int i = start; i < vertices.Count; i++)
                {
                    vertices[i] = fan.Start + facing * vertices[i];
                    normals[i] = facing * normals[i];
                }
            }
            else
            {
                float pace = child ? Range(.78f, .95f) : Range(.83f, 1.10f);
                float trip = Vector3.Distance(fan.Start, fan.End) / pace;
                walkers.Add(new Walker(fan.Start, fan.End, facing, start, vertices.Count, hip, hipY, shoulders, shoulderY,
                    h, trip, Range(0f, 2f * (trip + Walker.TurnSeconds)), fan.GroundHeights));
                if (walkers.Count == 1)
                    travelBounds = new Bounds(fan.Start, Vector3.zero);
                travelBounds.Encapsulate(fan.Start);
                travelBounds.Encapsulate(fan.End);
            }

            float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        }

        internal Mesh Build(string name)
        {
            mesh = new Mesh
            {
                name = name,
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            var uv = new Vector2[colors.Count];
            for (int i = 0; i < uv.Length; i++)
                uv[i] = palette.Coordinate(colors[i]);
            if (walking)
            {
                mesh.MarkDynamic();
                baseVertices = vertices.ToArray();
                baseNormals = normals.ToArray();
                currentVertices = new Vector3[vertices.Count];
                currentNormals = new Vector3[normals.Count];
                travelBounds.Expand(new Vector3(1.5f, 4.5f, 1.5f));
                SetWalkPoses(0f);
                mesh.vertices = currentVertices;
                mesh.normals = currentNormals;
            }
            else
            {
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
            }
            mesh.SetTriangles(triangles, 0);
            mesh.uv = uv;
            if (walking)
                mesh.bounds = travelBounds;
            return mesh;
        }

        internal void Update(float time)
        {
            SetWalkPoses(time);
            mesh.SetVertices(currentVertices, 0, currentVertices.Length, MeshUpdateFlags.DontRecalculateBounds);
            mesh.SetNormals(currentNormals);
        }

        private void SetWalkPoses(float time)
        {
            foreach (var person in walkers)
            {
                float half = person.Trip + Walker.TurnSeconds;
                float phase = Mathf.Repeat(time + person.Phase, 2f * half);
                bool returning = phase >= half;
                float segment = returning ? phase - half : phase;
                float progress = Mathf.SmoothStep(0f, 1f, segment / person.Trip);
                float turn = Mathf.SmoothStep(0f, 1f, (segment - person.Trip) / Walker.TurnSeconds);
                float routeProgress = returning ? 1f - progress : progress;
                Vector3 origin = Vector3.Lerp(person.Start, person.End, routeProgress);
                if (person.GroundHeights != null)
                {
                    float sample = routeProgress * (person.GroundHeights.Length - 1);
                    int index = Mathf.Min(Mathf.FloorToInt(sample), person.GroundHeights.Length - 2);
                    origin.y = Mathf.Lerp(person.GroundHeights[index], person.GroundHeights[index + 1], sample - index);
                }
                Quaternion yaw = Quaternion.AngleAxis((returning ? 180f : 0f) + 180f * turn, Vector3.up) * person.Facing;
                float phaseDistance = progress * person.Distance;
                float amplitude = Mathf.Clamp01(Mathf.Min(progress, 1f - progress) * 9f);
                float rhythm = phaseDistance * (Mathf.PI * 2f / (.86f * person.Height / 1.70f));
                float swing = Mathf.Sin(rhythm) * 18f * amplitude;
                origin.y += Mathf.Abs(Mathf.Sin(rhythm * 2f)) * .013f * amplitude;
                // Keep the lower toe above the path as each rigid shoe swings about its hip.
                float pitch = Mathf.Abs(swing) * Mathf.Deg2Rad;
                float hipY = person.LeftHip.y;
                float toeY = hipY + (person.Height * .002f - hipY) * Mathf.Cos(pitch) -
                    person.Height * .102f * Mathf.Sin(pitch);
                origin.y += Mathf.Max(0f, -toeY);
                Quaternion legLeft = Quaternion.AngleAxis(swing, Vector3.right);
                Quaternion legRight = Quaternion.AngleAxis(-swing, Vector3.right);
                Quaternion armLeft = Quaternion.AngleAxis(-swing * .64f, Vector3.right);
                Quaternion armRight = Quaternion.AngleAxis(swing * .64f, Vector3.right);
                for (int index = person.StartVertex; index < person.EndVertex; index++)
                {
                    Vector3 point = baseVertices[index], normal = baseNormals[index];
                    switch (bones[index])
                    {
                        case Bone.LeftLeg: Rotate(person.LeftHip, legLeft); break;
                        case Bone.RightLeg: Rotate(person.RightHip, legRight); break;
                        case Bone.LeftArm: Rotate(person.LeftShoulder, armLeft); break;
                        case Bone.RightArm: Rotate(person.RightShoulder, armRight); break;
                    }
                    currentVertices[index] = origin + yaw * point;
                    currentNormals[index] = yaw * normal;

                    void Rotate(Vector3 pivot, Quaternion rotation)
                    {
                        point = pivot + rotation * (point - pivot);
                        normal = rotation * normal;
                    }
                }
            }
        }

        private void Pipe(Vector3 start, Vector3 finish, float r0, float r1, int color, Bone bone, float bodyDepth = 1f)
        {
            const int sides = 6;
            Vector3 axis = (finish - start).normalized;
            Vector3 u = Vector3.Cross(Mathf.Abs(axis.y) > .9f ? Vector3.right : Vector3.up, axis).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            int first = vertices.Count;
            for (int i = 0; i < sides; i++)
            {
                Vector3 radial = Radial(i, sides, u, v);
                Vector3 adjust = new Vector3(radial.x, radial.y, radial.z * bodyDepth);
                Vector3 normal = new Vector3(radial.x, radial.y, radial.z / bodyDepth).normalized;
                Vertex(start + adjust * r0, normal, color, bone);
                Vertex(finish + adjust * r1, normal, color, bone);
            }
            int capA = Vertex(start, -axis, color, bone);
            int capB = Vertex(finish, axis, color, bone);
            for (int i = 0; i < sides; i++)
            {
                Vertex(vertices[first + i * 2], -axis, color, bone);
                Vertex(vertices[first + i * 2 + 1], axis, color, bone);
            }
            for (int i = 0; i < sides; i++)
            {
                int a = i * 2, b = ((i + 1) % sides) * 2;
                Quad(first + a, first + b, first + b + 1, first + a + 1);
                Triangle(capA, capB + 1 + b, capB + 1 + a);
                Triangle(capB, capB + 2 + a, capB + 2 + b);
            }
        }

        private void Globe(Vector3 center, Vector3 radius, int color, Bone bone)
        {
            const int sides = 7;
            int top = Vertex(center + Vector3.up * radius.y, Vector3.up, color, bone);
            int bottom = Vertex(center - Vector3.up * radius.y, Vector3.down, color, bone);
            int first = vertices.Count;
            for (int ring = -1; ring <= 1; ring += 2)
            for (int i = 0; i < sides; i++)
            {
                Vector3 radial = Radial(i, sides, Vector3.right, Vector3.back) * .8660254f + Vector3.up * (ring * .50f);
                Vector3 normal = new Vector3(radial.x / radius.x, radial.y / radius.y, radial.z / radius.z).normalized;
                Vertex(center + Vector3.Scale(radial, radius), normal, color, bone);
            }
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                Triangle(top, first + sides + i, first + sides + next);
                Triangle(bottom, first + next, first + i);
                Quad(first + sides + i, first + i, first + next, first + sides + next);
            }
        }

        private void Box(Vector3 center, Vector3 size, int color, Bone bone)
        {
            Vector3 h = size * .5f;
            var a = center + new Vector3(-h.x, -h.y, -h.z);
            var b = center + new Vector3(h.x, -h.y, -h.z);
            var c = center + new Vector3(h.x, -h.y, h.z);
            var d = center + new Vector3(-h.x, -h.y, h.z);
            var e = center + new Vector3(-h.x, h.y, -h.z);
            var f = center + new Vector3(h.x, h.y, -h.z);
            var g = center + new Vector3(h.x, h.y, h.z);
            var j = center + new Vector3(-h.x, h.y, h.z);
            AddFace(a, b, c, d, Vector3.down);
            AddFace(e, j, g, f, Vector3.up);
            AddFace(a, d, j, e, Vector3.left);
            AddFace(c, b, f, g, Vector3.right);
            AddFace(d, c, g, j, Vector3.forward);
            AddFace(b, a, e, f, Vector3.back);

            void AddFace(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal) => Quad(
                Vertex(p0, normal, color, bone), Vertex(p1, normal, color, bone),
                Vertex(p2, normal, color, bone), Vertex(p3, normal, color, bone));
        }

        private int Vertex(Vector3 point, Vector3 normal, int color, Bone bone)
        {
            int index = vertices.Count;
            vertices.Add(point);
            normals.Add(normal);
            colors.Add(color);
            if (walking)
                bones.Add(bone);
            return index;
        }

        private static Vector3 Radial(int index, int sides, Vector3 u, Vector3 v)
        {
            float angle = index * (2f * Mathf.PI / sides);
            return u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
        }

        private void Quad(int a, int b, int c, int d)
        {
            Triangle(a, b, c);
            Triangle(a, c, d);
        }

        private void Triangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        private readonly struct Walker
        {
            internal const float TurnSeconds = .72f;
            internal readonly Vector3 Start, End, LeftHip, RightHip, LeftShoulder, RightShoulder;
            internal readonly Quaternion Facing;
            internal readonly int StartVertex, EndVertex;
            internal readonly float Height, Distance, Trip, Phase;
            internal readonly float[] GroundHeights;

            internal Walker(Vector3 start, Vector3 end, Quaternion facing, int startVertex, int endVertex,
                float hip, float hipY, float shoulders, float shoulderY, float height, float trip, float phase,
                float[] groundHeights)
            {
                Start = start;
                End = end;
                Facing = facing;
                StartVertex = startVertex;
                EndVertex = endVertex;
                LeftHip = new Vector3(-hip, hipY, 0f);
                RightHip = new Vector3(hip, hipY, 0f);
                LeftShoulder = new Vector3(-shoulders, shoulderY, 0f);
                RightShoulder = new Vector3(shoulders, shoulderY, 0f);
                Height = height;
                Distance = Vector3.Distance(start, end);
                Trip = trip;
                Phase = phase;
                GroundHeights = groundHeights;
            }
        }
    }
}
