using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class TrackPreview
{
    // Both facades have bays. The front faces -Z and the rear faces +Z.
    private void AddInfieldGarageBuilding(Transform parent, bool texas = false, int style = 0)
    {
        const float halfLength = 55f;
        const float front = -7.20f;
        const float back = 7.20f;
        const float roof = 5.43f;
        const float sideWall = halfLength - 0.24f;
        const float pitch = 5.25f;
        const float doorHalfWidth = 1.98f;
        const int bays = 20;
        const float firstBay = -(bays - 1) * pitch * 0.5f;

        (Color32 Wall, Color32 Door, Color32 Trim, Color32 Roof) palette = style switch
        {
            1 => (new Color32(188, 177, 159, 255), new Color32(94, 104, 111, 255), new Color32(166, 125, 84, 255), new Color32(117, 113, 106, 255)),
            2 => (new Color32(166, 177, 177, 255), new Color32(100, 119, 128, 255), new Color32(73, 113, 132, 255), new Color32(103, 116, 121, 255)),
            3 => (new Color32(183, 174, 164, 255), new Color32(119, 122, 120, 255), new Color32(139, 82, 78, 255), new Color32(113, 104, 101, 255)),
            4 => (new Color32(176, 183, 169, 255), new Color32(100, 118, 112, 255), new Color32(98, 125, 111, 255), new Color32(100, 111, 107, 255)),
            5 => (new Color32(189, 187, 175, 255), new Color32(114, 128, 140, 255), new Color32(150, 161, 174, 255), new Color32(117, 123, 130, 255)),
            6 => (new Color32(175, 172, 164, 255), new Color32(91, 97, 106, 255), new Color32(131, 111, 135, 255), new Color32(106, 102, 109, 255)),
            _ => (new Color32(184, 179, 166, 255), new Color32(132, 141, 145, 255), new Color32(194, 197, 190, 255), new Color32(100, 106, 104, 255)),
        };
        var model = new GarageBuildingMesh();
        // The wall grain is shared with concrete elsewhere in the track preview.
        var concrete = BristolPavementTexture(true);
        var surfaces = new[]
        {
            GarageMaterial(texas ? new Color32(194, 181, 158, 255) : palette.Wall, 0.12f, concrete),
            GarageMaterial(texas ? new Color32(183, 171, 151, 255) : style == 0 ? new Color32(175, 172, 163, 255) : Darken(palette.Wall, 8), 0.12f, concrete),
            GarageMaterial(texas ? new Color32(126, 117, 104, 255) : new Color32(128, 127, 118, 255), 0.10f, concrete),
            GarageMaterial(texas ? new Color32(76, 82, 85, 255) : palette.Door, 0.26f),
            GarageMaterial(texas ? new Color32(58, 67, 70, 255) : style == 0 ? new Color32(106, 115, 121, 255) : Darken(palette.Door, 23), 0.22f),
            GarageMaterial(new Color32(57, 64, 65, 255), 0.18f),
            GarageMaterial(texas ? new Color32(147, 98, 81, 255) : palette.Trim, 0.30f),
            GarageMaterial(new Color32(49, 68, 75, 255), 0.51f),
            GarageMaterial(texas ? new Color32(111, 106, 94, 255) : palette.Roof, 0.17f),
            GarageMaterial(new Color32(204, 162, 64, 255), 0.23f),
        };

        model.Box(-halfLength, 0f, front + 0.22f, halfLength, 0.22f, back, GarageSurface.Foundation);
        model.Box(-sideWall, 0.20f, front, -sideWall + 0.27f, roof, back, GarageSurface.Concrete);
        model.Box(sideWall - 0.27f, 0.20f, front, sideWall, roof, back, GarageSurface.Concrete);
        BuildFacade(front, -1f);
        BuildFacade(back, 1f);
        AddCorner(-halfLength + 0.24f);
        AddCorner(halfLength - 0.24f);
        AddEquipment(1f, -5.15f);
        AddEquipment(1f, 4.70f);
        AddEquipment(-1f, 4.70f);

        model.Box(-halfLength, roof - 0.18f, front - 0.25f, halfLength, roof + 0.04f, back + 0.25f,
            GarageSurface.Roof);
        model.Box(-halfLength, roof + 0.04f, front - 0.28f, halfLength, 5.95f, front + 0.04f,
            GarageSurface.Concrete);
        model.Box(-halfLength, roof + 0.04f, back - 0.04f, halfLength, 5.88f, back + 0.28f,
            GarageSurface.Concrete);
        model.Box(-halfLength, roof + 0.04f, front + 0.04f, -halfLength + 0.25f, 5.88f, back - 0.04f,
            GarageSurface.Concrete);
        model.Box(halfLength - 0.25f, roof + 0.04f, front + 0.04f, halfLength, 5.88f, back - 0.04f,
            GarageSurface.Concrete);
        model.Box(-halfLength, 5.95f, front - 0.30f, halfLength, 6.04f, front + 0.08f, GarageSurface.Trim);
        model.Box(-halfLength, 5.88f, back - 0.08f, halfLength, 5.97f, back + 0.30f, GarageSurface.Trim);
        model.Box(-halfLength, 5.88f, front + 0.08f, -halfLength + 0.29f, 5.97f, back - 0.08f, GarageSurface.Trim);
        model.Box(halfLength - 0.29f, 5.88f, front + 0.08f, halfLength, 5.97f, back - 0.08f, GarageSurface.Trim);

        if (texas || style is 2 or 3 or 4 or 6)
        {
            FrontBox(-halfLength, 5.55f, halfLength, 5.79f, front - 0.27f, -1f, 0.030f, GarageSurface.Trim);
            FrontBox(-halfLength, 5.55f, halfLength, 5.79f, back + 0.27f, 1f, 0.030f, GarageSurface.Trim);
        }

        // The ribbed roof and vent boxes keep the building legible from the overhead camera.
        for (float x = -halfLength + 2.5f; x < halfLength - 2f; x += 3.10f)
            model.Box(x - 0.027f, roof + 0.04f, front + 0.21f, x + 0.027f, roof + 0.09f, back - 0.21f,
                GarageSurface.Door);
        for (int vent = 0; vent < 4; vent++)
        {
            float x = -40.5f + vent * 27f;
            float z = vent % 2 == 0 ? 1.4f : -1.2f;
            model.Box(x - 1.28f, roof + 0.04f, z - 0.77f, x + 1.28f, 5.57f, z + 0.77f, GarageSurface.DarkMetal);
            model.Box(x - 1.16f, 5.57f, z - 0.65f, x + 1.16f, 6.03f, z + 0.65f, GarageSurface.Trim);
            model.Box(x - 1.06f, 5.66f, z - 0.664f, x + 1.06f, 5.94f, z - 0.65f, GarageSurface.DarkMetal);
            for (int slat = 0; slat < 4; slat++)
            {
                float y = 5.68f + slat * 0.065f;
                model.Box(x - 1.06f, y, z - 0.691f, x + 1.06f, y + 0.022f, z - 0.664f, GarageSurface.Door);
            }
        }

        Mesh mesh = model.Finish(parent.name + " building");
        roadMeshes.Add(mesh);
        var building = CreateObject("Garage building", parent);
        building.AddComponent<MeshFilter>().sharedMesh = mesh;
        building.AddComponent<MeshRenderer>().sharedMaterials = surfaces;

        static Color32 Darken(Color32 color, int amount) => new Color32(
            (byte)Mathf.Max(0, color.r - amount), (byte)Mathf.Max(0, color.g - amount),
            (byte)Mathf.Max(0, color.b - amount), 255);

        Material GarageMaterial(Color color, float smoothness, Texture texture = null)
        {
            Material material = MakeMaterial(color);
            material.SetFloat("_Smoothness", smoothness);
            if (texture != null)
                material.SetTexture("_BaseMap", texture);
            return material;
        }

        void BuildFacade(float wall, float facing)
        {
            float begin = firstBay - pitch * 0.5f;
            float end = -begin;
            FacadeBox(-halfLength, 0.20f, begin, roof, wall, facing, -0.25f, 0f, GarageSurface.Concrete);
            FacadeBox(end, 0.20f, halfLength, roof, wall, facing, -0.25f, 0f, GarageSurface.AlternateConcrete);
            for (int bay = 0; bay < bays; bay++)
            {
                float x = firstBay + bay * pitch;
                float left = x - pitch * 0.5f;
                float right = x + pitch * 0.5f;
                GarageSurface panel = bay % 3 == 1 ? GarageSurface.AlternateConcrete : GarageSurface.Concrete;
                FacadeBox(left + 0.013f, 0.20f, x - doorHalfWidth, 4.15f, wall, facing, -0.25f, 0f, panel);
                FacadeBox(x + doorHalfWidth, 0.20f, right - 0.013f, 4.15f, wall, facing, -0.25f, 0f, panel);
                FacadeBox(left + 0.013f, 4.15f, right - 0.013f, roof, wall, facing, -0.25f, 0f, panel);
                // Recess the joint backing behind the concrete face.
                if (bay != 0)
                    FacadeBox(left - 0.015f, 0.18f, left + 0.015f, roof, wall, facing, -0.24f, -0.040f,
                        GarageSurface.AlternateConcrete);

                AddGarageBay(x, wall, facing);
                float halfWindow = texas && bay % 3 == 1 ? 1.42f : 1.84f;
                AddClerestory(x, wall, facing, 4.16f, halfWindow);
                if (bay % 3 == 0)
                {
                    float pipeX = left + 0.26f;
                    FacadeBox(pipeX - 0.045f, 0.27f, pipeX + 0.045f, 5.34f, wall, facing, 0.06f, 0.17f,
                        GarageSurface.DarkMetal);
                    FacadeBox(pipeX - 0.045f, 0.23f, pipeX + 0.045f, 0.34f, wall, facing, 0.06f, 0.25f,
                        GarageSurface.DarkMetal);
                }
            }

            AddPersonDoor(-halfLength + 1.36f, wall, facing);
            AddPersonDoor(halfLength - 1.36f, wall, facing);
        }

        void AddGarageBay(float x, float wall, float facing)
        {
            var shadow = GarageSurface.DarkMetal;
            float left = x - doorHalfWidth;
            float right = x + doorHalfWidth;
            FacadeBox(left, 0.105f, right, 4.15f, wall, facing, -0.23f, -0.025f, shadow);
            FacadeBox(left + 0.115f, 0.15f, right - 0.115f, 3.96f, wall, facing, -0.045f, 0f,
                GarageSurface.Door);
            FacadeBox(left - 0.105f, 0.08f, left + 0.127f, 4.10f, wall, facing, -0.08f, 0.155f, shadow);
            FacadeBox(right - 0.127f, 0.08f, right + 0.105f, 4.10f, wall, facing, -0.08f, 0.155f, shadow);
            FacadeBox(left - 0.105f, 3.96f, right + 0.105f, 4.13f, wall, facing, -0.08f, 0.23f, GarageSurface.Trim);
            FacadeBox(left + 0.125f, 0.10f, right - 0.125f, 0.17f, wall, facing, -0.03f, 0.028f, shadow);
            for (int slat = 0; slat < 18; slat++)
            {
                float y = 0.34f + slat * 0.203f;
                FacadeBox(left + 0.138f, y, right - 0.138f, y + 0.027f, wall, facing, -0.002f, 0.029f,
                    GarageSurface.DoorRib);
            }
            FacadeBox(x - 0.18f, 0.41f, x + 0.18f, 0.49f, wall, facing, 0.012f, 0.061f, shadow);
            FacadeBox(x - 0.29f, 4.78f, x + 0.29f, 4.93f, wall, facing, 0.02f, 0.19f, shadow);
            FacadeBox(x - 0.24f, 4.78f, x + 0.24f, 4.85f, wall, facing, 0.16f, 0.21f, GarageSurface.Trim);
            FacadeBox(left - 0.40f, 0f, left - 0.24f, 0.91f, wall, facing, 0.14f, 0.30f, GarageSurface.Bollard);
            FacadeBox(right + 0.24f, 0f, right + 0.40f, 0.91f, wall, facing, 0.14f, 0.30f, GarageSurface.Bollard);
        }

        void AddClerestory(float x, float wall, float facing, float level, float halfWindow)
        {
            FrontBox(x - halfWindow, level, x + halfWindow, level + 0.63f, wall, facing, 0.064f,
                GarageSurface.DarkMetal);
            FrontBox(x - halfWindow + 0.085f, level + 0.077f, x + halfWindow - 0.085f, level + 0.55f,
                wall + facing * 0.06f, facing, 0.019f, GarageSurface.Glass);
            for (int mullion = -1; mullion <= 1; mullion++)
            {
                float location = x + mullion * Mathf.Min(0.88f, halfWindow * 0.48f);
                FrontBox(location - 0.026f, level + 0.075f, location + 0.026f, level + 0.55f,
                    wall + facing * 0.074f, facing, 0.014f, GarageSurface.Trim);
            }
            FrontBox(x - halfWindow - 0.07f, level - 0.05f, x + halfWindow + 0.07f, level + 0.035f,
                wall, facing, 0.13f, GarageSurface.Trim);
        }

        void AddPersonDoor(float x, float wall, float facing)
        {
            FrontBox(x - 0.59f, 0.07f, x + 0.59f, 2.25f, wall, facing, 0.078f, GarageSurface.DarkMetal);
            FrontBox(x - 0.49f, 0.12f, x + 0.49f, 2.15f, wall + facing * 0.074f, facing, 0.026f,
                GarageSurface.Door);
            FrontBox(x - 0.35f, 1.43f, x + 0.35f, 1.97f, wall + facing * 0.097f, facing, 0.021f,
                GarageSurface.DarkMetal);
            FrontBox(x - 0.285f, 1.50f, x + 0.285f, 1.90f, wall + facing * 0.116f, facing, 0.009f,
                GarageSurface.Glass);
            FrontBox(x - 0.36f, 0.98f, x - 0.15f, 1.035f, wall + facing * 0.099f, facing, 0.042f,
                GarageSurface.Trim);
            FrontBox(x - 0.49f, 0.16f, x + 0.49f, 0.36f, wall + facing * 0.10f, facing, 0.016f,
                GarageSurface.Trim);
        }

        void FrontBox(float x0, float y0, float x1, float y1, float wall, float facing, float depth,
            GarageSurface surface)
        {
            FacadeBox(x0, y0, x1, y1, wall, facing, 0f, depth, surface);
        }

        void FacadeBox(float x0, float y0, float x1, float y1, float wall, float facing,
            float innerDepth, float outerDepth, GarageSurface surface)
        {
            float inner = wall + facing * innerDepth;
            float outer = wall + facing * outerDepth;
            model.Box(x0, y0, Mathf.Min(inner, outer), x1, y1, Mathf.Max(inner, outer), surface);
        }

        void AddCorner(float x)
        {
            model.Box(x - 0.23f, 0.11f, front - 0.16f, x + 0.23f, 5.56f, front + 0.06f,
                GarageSurface.AlternateConcrete);
            model.Box(x - 0.23f, 0.11f, back - 0.06f, x + 0.23f, 5.56f, back + 0.16f,
                GarageSurface.AlternateConcrete);
            model.Box(x - 0.235f, 0f, front - 0.20f, x + 0.235f, 0.25f, front + 0.06f,
                GarageSurface.Foundation);
            model.Box(x - 0.235f, 0f, back - 0.06f, x + 0.235f, 0.25f, back + 0.20f,
                GarageSurface.Foundation);
        }

        void AddEquipment(float facing, float z)
        {
            float wall = facing * sideWall;
            float casing = wall + facing * 0.146f;
            model.Box(Mathf.Min(wall, casing), 2.82f, z - 0.92f, Mathf.Max(wall, casing), 3.83f, z + 0.92f,
                GarageSurface.DarkMetal);
            float grille = casing + facing * 0.045f;
            for (int slat = 0; slat < 7; slat++)
            {
                float y = 2.90f + slat * 0.132f;
                model.Box(Mathf.Min(grille, casing), y, z - 0.81f, Mathf.Max(grille, casing), y + 0.06f, z + 0.81f,
                    GarageSurface.DoorRib);
            }
        }
    }

    private enum GarageSurface
    {
        Concrete, AlternateConcrete, Foundation, Door, DoorRib, DarkMetal, Trim, Glass, Roof, Bollard
    }

    // Flat normals keep the formed concrete and pressed metal edges crisp.
    private sealed class GarageBuildingMesh
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> coordinates = new List<Vector2>();
        private readonly List<int>[] triangles = new List<int>[(int)GarageSurface.Bollard + 1];

        internal void Box(float x0, float y0, float z0, float x1, float y1, float z1, GarageSurface surface)
        {
            var a = new Vector3(x0, y0, z0);
            var b = new Vector3(x1, y0, z0);
            var c = new Vector3(x0, y1, z0);
            var d = new Vector3(x0, y0, z1);
            var e = new Vector3(x1, y1, z0);
            var f = new Vector3(x1, y0, z1);
            var g = new Vector3(x0, y1, z1);
            var h = new Vector3(x1, y1, z1);
            Quad(a, c, e, b, Vector3.back, surface);
            Quad(d, f, h, g, Vector3.forward, surface);
            Quad(a, d, g, c, Vector3.left, surface);
            Quad(b, e, h, f, Vector3.right, surface);
            Quad(c, g, h, e, Vector3.up, surface);
            Quad(a, b, f, d, Vector3.down, surface);
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, GarageSurface surface)
        {
            int first = vertices.Count;
            Add(a);
            Add(b);
            Add(c);
            Add(d);
            var group = triangles[(int)surface] ??= new List<int>();
            group.Add(first);
            group.Add(first + 1);
            group.Add(first + 2);
            group.Add(first);
            group.Add(first + 2);
            group.Add(first + 3);

            void Add(Vector3 value)
            {
                vertices.Add(value);
                normals.Add(normal);
                coordinates.Add((normal.y != 0f ? new Vector2(value.x, value.z)
                    : new Vector2(normal.x != 0f ? value.z : value.x, value.y)) / 2.6f);
            }
        }

        internal Mesh Finish(string name)
        {
            var mesh = new Mesh
            {
                name = name,
                indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                subMeshCount = triangles.Length
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, coordinates);
            for (int i = 0; i < triangles.Length; i++)
                mesh.SetTriangles(triangles[i], i, false);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
