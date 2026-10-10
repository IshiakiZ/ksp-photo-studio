using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhotoStudio
{
    /// <summary>Everything the studio has made (meshes, materials, textures, objects), to be destroyed together.</summary>
    sealed class Made
    {
        readonly List<UnityEngine.Object> things = new List<UnityEngine.Object>();

        public T Keep<T>(T thing) where T : UnityEngine.Object
        {
            if (thing != null) things.Add(thing);
            return thing;
        }

        public void Destroy()
        {
            for (int i = things.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object thing = things[i];
                if (thing == null) continue;
                if (thing is RenderTexture rt) rt.Release();
                UnityEngine.Object.Destroy(thing);
            }
            things.Clear();
        }
    }

    /// <summary>The shaders the studio draws with: all of them the game's own. None is needed that the game does not already have.</summary>
    static class Shaders
    {
        static Shader flat, blended;
        static bool looked;
        public static string Missing = "";

        /// <summary>A shader that draws a thing's own picture in its own colour, with no light on it (the backdrops, the softboxes).</summary>
        public static Shader Flat { get { Look(); return flat; } }
        /// <summary>A shader that lays a picture over what is behind it, by its transparency (the shadow on the floor, the floor over its reflection).</summary>
        public static Shader Blended { get { Look(); return blended; } }

        public static bool Ready { get { Look(); return flat != null && blended != null; } }

        static void Look()
        {
            if (looked && flat != null && blended != null) return;
            looked = true;
            flat = First("KSP/Unlit", "Unlit/Texture", "Unlit/Transparent Cutout");
            blended = First("KSP/Particles/Alpha Blended", "Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended");
            Missing = (flat == null ? "a flat shader (KSP/Unlit) " : "") + (blended == null ? "a see-through shader (KSP/Particles/Alpha Blended)" : "");
        }

        static Shader First(params string[] names)
        {
            foreach (string name in names)
            {
                Shader found = Shader.Find(name);
                if (found != null && found.isSupported) return found;
            }
            return null;
        }
    }

    /// <summary>Making the pieces of a set: meshes worked out here, pictures painted here, and materials of the game's own shaders.</summary>
    static class Workshop
    {
        public static readonly int Colour = Shader.PropertyToID("_Color"), Tint = Shader.PropertyToID("_TintColor"), Picture = Shader.PropertyToID("_MainTex"), Cutoff = Shader.PropertyToID("_Cutoff");

        /// <summary>A material that shows a picture in a colour, unlit: what it looks like does not depend on any light.</summary>
        public static Material Flat(Made made, Texture picture, Color colour, string name)
        {
            var m = made.Keep(new Material(Shaders.Flat) { name = "Photo Studio " + name });
            if (m.HasProperty(Picture)) m.SetTexture(Picture, picture);
            if (m.HasProperty(Colour)) m.SetColor(Colour, colour);
            if (m.HasProperty(Cutoff)) m.SetFloat(Cutoff, 0f);
            return m;
        }

        /// <summary>A see-through material: picture times vertex colour, laid over what is behind it by its alpha.</summary>
        public static Material Blended(Made made, Texture picture, string name)
        {
            var m = made.Keep(new Material(Shaders.Blended) { name = "Photo Studio " + name });
            if (m.HasProperty(Picture)) m.SetTexture(Picture, picture);
            // (the particle shaders take twice their tint: half is "as it is")
            if (m.HasProperty(Tint)) m.SetColor(Tint, new Color(0.5f, 0.5f, 0.5f, 0.5f));
            if (m.HasProperty(Colour)) m.SetColor(Colour, Color.white);
            return m;
        }

        public static Texture2D Solid(Made made, Color colour)
        {
            var t = made.Keep(new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Photo Studio solid", wrapMode = TextureWrapMode.Clamp });
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = colour;
            t.SetPixels(pixels);
            t.Apply(false, true);
            return t;
        }

        /// <summary>A picture that runs from colour to colour along its height (its v, 0 to 1), each colour at the place given.</summary>
        public static Texture2D Ramp(Made made, float[] at, Color[] colours)
        {
            const int High = 512;
            var t = made.Keep(new Texture2D(2, High, TextureFormat.RGBA32, false) { name = "Photo Studio ramp", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear });
            var pixels = new Color[2 * High];
            for (int y = 0; y < High; y++)
            {
                float v = y / (High - 1f);
                int i = 0;
                while (i < at.Length - 2 && v > at[i + 1]) i++;
                float span = Mathf.Max(at[i + 1] - at[i], 1e-5f);
                Color c = Color.Lerp(colours[i], colours[i + 1], Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((v - at[i]) / span)));
                pixels[2 * y] = pixels[2 * y + 1] = c;
            }
            t.SetPixels(pixels);
            t.Apply(false, true);
            return t;
        }

        /// <summary>
        /// The front of a softbox: a diffuser bright in the middle and a little less so towards its edges, as a real one is,
        /// inside a thin dark frame.
        /// </summary>
        public static Texture2D Softbox(Made made, int wide, int high, float frame)
        {
            var t = made.Keep(new Texture2D(wide, high, TextureFormat.RGBA32, true) { name = "Photo Studio softbox", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 });
            var pixels = new Color[wide * high];
            for (int y = 0; y < high; y++)
                for (int x = 0; x < wide; x++)
                {
                    float u = (x + 0.5f) / wide, v = (y + 0.5f) / high;
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u) * wide, Mathf.Min(v, 1f - v) * high);          // pixels from the nearest edge
                    float frameWide = frame * Mathf.Min(wide, high);
                    float glow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((edge - frameWide) / (frameWide * 1.5f + 1f)));
                    float middle = 1f - 0.10f * ((2f * u - 1f) * (2f * u - 1f) + (2f * v - 1f) * (2f * v - 1f));
                    float value = Mathf.Lerp(0.035f, middle, glow);
                    pixels[y * wide + x] = new Color(value, value, value, 1f);
                }
            t.SetPixels(pixels);
            t.Apply(true, true);
            return t;
        }

        /// <summary>
        /// A surface turned round the upright axis: a line in (distance from the axis, height) swept once round. Its faces
        /// look inwards and upwards (it is seen from inside). v of each point is how far along the line it is, 0 to 1.
        /// </summary>
        public static Mesh Turned(Made made, IList<Vector2> line, int round, string name)
        {
            int n = line.Count, ring = round + 1;
            var points = new Vector3[n * ring];
            var uv = new Vector2[n * ring];
            float total = 0f;
            var along = new float[n];
            for (int i = 1; i < n; i++) { total += Vector2.Distance(line[i], line[i - 1]); along[i] = total; }
            for (int i = 0; i < n; i++)
                for (int j = 0; j <= round; j++)
                {
                    float a = j * 2f * Mathf.PI / round;
                    points[i * ring + j] = new Vector3(line[i].x * Mathf.Cos(a), line[i].y, line[i].x * Mathf.Sin(a));
                    uv[i * ring + j] = new Vector2(j / (float)round, total > 0f ? along[i] / total : 0f);
                }
            var triangles = new List<int>((n - 1) * round * 6);
            for (int i = 0; i < n - 1; i++)
                for (int j = 0; j < round; j++)
                {
                    int a = i * ring + j, b = a + 1, c = a + ring + 1, d = a + ring;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(a); triangles.Add(c); triangles.Add(d);
                }
            var mesh = made.Keep(new Mesh { name = "Photo Studio " + name });
            if (points.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = points;
            mesh.uv = uv;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The line of a cyclorama, outwards: flat floor from `start` (nought: the middle) to `floor`, then a quarter circle
        /// of radius `bend` up into an upright wall, which goes on to `top`; then, if `roof` is more than nought, a ceiling
        /// at that height back over the middle.
        /// </summary>
        public static List<Vector2> CoveLine(float start, float floor, float bend, float top, float roof, int floorSteps, int bendSteps)
        {
            var line = new List<Vector2> { new Vector2(start, 0f) };
            for (int i = 1; i <= floorSteps; i++) line.Add(new Vector2(Mathf.Lerp(start, floor, i / (float)floorSteps), 0f));
            for (int i = 1; i <= bendSteps; i++)
            {
                float a = 0.5f * Mathf.PI * i / bendSteps;
                line.Add(new Vector2(floor + bend * Mathf.Sin(a), bend - bend * Mathf.Cos(a)));
            }
            float wall = floor + bend;
            for (int i = 1; i <= 4; i++) line.Add(new Vector2(wall, Mathf.Lerp(bend, top, i / 4f)));
            if (roof > 0f)
            {
                line.Add(new Vector2(wall, roof));
                for (int i = 1; i <= 6; i++) line.Add(new Vector2(wall * (1f - i / 6f), roof));
            }
            return line;
        }

        /// <summary>A flat ring lying on the floor, its faces up.</summary>
        public static Mesh Ring(Made made, float inner, float outer, int round, string name)
        {
            return Turned(made, new[] { new Vector2(inner, 0f), new Vector2(outer, 0f) }, round, name);
        }

        /// <summary>
        /// A flat round floor, its faces up, its rings closer together near the middle (where it is looked at closely)
        /// than near its edge, with a colour at every point that can be changed each frame.
        /// </summary>
        public static Mesh Floor(Made made, float radius, int rings, int round, string name)
        {
            var line = new List<Vector2> { new Vector2(0f, 0f) };
            for (int i = 1; i <= rings; i++) line.Add(new Vector2(radius * Mathf.Pow(i / (float)rings, 1.7f), 0f));
            Mesh mesh = Turned(made, line, round, name);
            var colours = new Color32[mesh.vertexCount];
            for (int i = 0; i < colours.Length; i++) colours[i] = new Color32(0, 0, 0, 255);
            mesh.colors32 = colours;
            mesh.MarkDynamic();
            return mesh;
        }

        /// <summary>A flat rectangle, its middle at nought, lying in x and y and facing -z (towards whoever stands at -z looking along +z), its picture the right way up.</summary>
        public static Mesh Card(Made made, float wide, float high, string name)
        {
            var mesh = made.Keep(new Mesh { name = "Photo Studio " + name });
            float x = 0.5f * wide, y = 0.5f * high;
            mesh.vertices = new[] { new Vector3(-x, -y, 0f), new Vector3(x, -y, 0f), new Vector3(-x, y, 0f), new Vector3(x, y, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 3, 1, 3, 0, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A box, its middle at nought, its faces outwards.</summary>
        public static Mesh Box(Made made, Vector3 size, string name)
        {
            var mesh = made.Keep(new Mesh { name = "Photo Studio " + name });
            Vector3 h = 0.5f * size;
            var points = new List<Vector3>();
            var triangles = new List<int>();
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (Vector3 n in normals)
            {
                // (two ways along the face, u and r = u x n; the corners taken in the order that makes the face look along +n:
                // in Unity a triangle A B C is seen from the side that (B - A) x (C - A) points to)
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 r = Vector3.Cross(u, n);
                Vector3 centre = Vector3.Scale(n, h);
                Vector3 du = Vector3.Scale(u, h), dr = Vector3.Scale(r, h);
                int k = points.Count;
                points.Add(centre - du - dr); points.Add(centre - du + dr); points.Add(centre + du - dr); points.Add(centre + du + dr);
                triangles.Add(k); triangles.Add(k + 1); triangles.Add(k + 3);
                triangles.Add(k); triangles.Add(k + 3); triangles.Add(k + 2);
            }
            mesh.SetVertices(points);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A thing of the set: a mesh drawn with a material, under a parent, on a layer, casting and taking no shadows.</summary>
        public static GameObject Piece(string name, Transform parent, Mesh mesh, Material material, int layer)
        {
            var thing = new GameObject("Photo Studio " + name);
            thing.layer = layer;
            thing.transform.SetParent(parent, false);
            thing.AddComponent<MeshFilter>().sharedMesh = mesh;
            var drawn = thing.AddComponent<MeshRenderer>();
            drawn.sharedMaterial = material;
            drawn.shadowCastingMode = ShadowCastingMode.Off;
            drawn.receiveShadows = false;
            drawn.lightProbeUsage = LightProbeUsage.Off;
            drawn.reflectionProbeUsage = ReflectionProbeUsage.Off;
            drawn.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            return thing;
        }
    }
}
