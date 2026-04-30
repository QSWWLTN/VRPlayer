using Godot;

namespace VRPlayerProject.Rendering;

public static class SphereGenerator
{
    public static ArrayMesh GenerateInvertedSphere(float radius = 50f, int segmentsH = 64, int segmentsV = 32)
    {
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        for (int y = 0; y <= segmentsV; y++)
        {
            float v = y / (float)segmentsV;
            float theta = v * Mathf.Pi;

            for (int x = 0; x <= segmentsH; x++)
            {
                float u = x / (float)segmentsH;
                float phi = u * 2.0f * Mathf.Pi;

                float sx = radius * Mathf.Sin(theta) * Mathf.Cos(phi);
                float sy = radius * Mathf.Cos(theta);
                float sz = radius * Mathf.Sin(theta) * Mathf.Sin(phi);

                var vertex = new Vector3(sx, sy, sz);
                var normal = -vertex.Normalized();
                var uv = new Vector2(u, v);

                surfaceTool.SetNormal(normal);
                surfaceTool.SetUV(uv);
                surfaceTool.AddVertex(vertex);
            }
        }

        for (int y = 0; y < segmentsV; y++)
        {
            for (int x = 0; x < segmentsH; x++)
            {
                int i0 = y * (segmentsH + 1) + x;
                int i1 = i0 + 1;
                int i2 = (y + 1) * (segmentsH + 1) + x;
                int i3 = i2 + 1;

                surfaceTool.AddIndex(i0);
                surfaceTool.AddIndex(i2);
                surfaceTool.AddIndex(i1);

                surfaceTool.AddIndex(i1);
                surfaceTool.AddIndex(i2);
                surfaceTool.AddIndex(i3);
            }
        }

        return surfaceTool.Commit();
    }

    public static ArrayMesh GenerateQuad(float width = 16f, float height = 9f)
    {
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        float hw = width / 2f;
        float hh = height / 2f;

        Vector3[] verts =
        {
            new(-hw, -hh, 0), new(hw, -hh, 0),
            new(hw, hh, 0), new(-hw, hh, 0)
        };

        Vector2[] uvs =
        {
            new(0, 1), new(1, 1),
            new(1, 0), new(0, 0)
        };

        int[] indices = { 0, 1, 2, 0, 2, 3 };

        foreach (int i in indices)
        {
            surfaceTool.SetUV(uvs[i]);
            surfaceTool.AddVertex(verts[i]);
        }

        return surfaceTool.Commit();
    }
}
