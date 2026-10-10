using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace PhotoStudio
{
    /// <summary>
    /// The soft shadow on the floor under the craft, as a studio's large soft lights leave it: darkest where the craft
    /// comes down to the floor, and fading out round it. Worked out from the craft itself: from each spot of the floor
    /// under and round the craft, lines are drawn up into the half of the sky above it, in many directions (more of them
    /// steeply up than low, as the light that falls on a floor comes); the more of them meet a part of the craft, and the
    /// nearer, the darker that spot. (Rendering people call this ambient occlusion.) The lines are the game's own ray
    /// casts against the parts' colliders, a few thousand a frame until all are done; then the shadow fades in.
    /// </summary>
    sealed class Shade
    {
        const int Rays = 32;
        const float Under = 0.06f;
        public readonly int Wide, Deep;
        public readonly Vector2 Low, Size;                     // the floor's patch: its corner (x, z) and its size (x, z) in metres
        readonly float floor, reach;                           // the floor's height and how far up a part still shades it, in the studio's axes
        readonly Vector3 origin;
        readonly Quaternion frame;
        readonly float[] dark;
        readonly Vector3[] directions = new Vector3[Rays];
        int next;
        public bool Done { get; private set; }
        public Texture2D Picture { get; private set; }

        readonly HashSet<Collider> ignored;
        readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>`ignored`: colliders that cast no shadow (launch clamps, which are hidden in the studio); nothing for none.</summary>
        /// <summary>`craft`: its box in the studio's axes (`frame`, turned from the world's), measured from `origin`.</summary>
        public Shade(Bounds craft, Vector3 origin, Quaternion frame, Made made, HashSet<Collider> ignored)
        {
            this.origin = origin;
            this.frame = frame;
            this.ignored = ignored != null && ignored.Count > 0 ? ignored : null;
            float across = Mathf.Max(craft.size.x, craft.size.z), tall = craft.size.y;
            // (how far up a part still shades the floor: further for a big craft, never more than a few metres)
            reach = Mathf.Clamp(0.22f * Mathf.Max(tall, across), 1.2f, 9f);
            float margin = reach * 0.9f + 0.3f;
            Low = new Vector2(craft.min.x - margin, craft.min.z - margin);
            Size = new Vector2(craft.size.x + 2f * margin, craft.size.z + 2f * margin);
            float cell = Mathf.Max(Size.x, Size.y) / 56f;
            Wide = Mathf.Clamp(Mathf.CeilToInt(Size.x / cell), 8, 72);
            Deep = Mathf.Clamp(Mathf.CeilToInt(Size.y / cell), 8, 72);
            floor = craft.min.y;
            dark = new float[Wide * Deep];
            // Directions over the upper half of the sky, as many in each part as the light from there that falls on a floor
            // (more steeply up than low down): points spread evenly over a disc, lifted onto the half sphere above it.
            float turn = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < Rays; i++)
            {
                float r = Mathf.Sqrt((i + 0.5f) / Rays), a = i * turn;
                float x = r * Mathf.Cos(a), z = r * Mathf.Sin(a);
                directions[i] = new Vector3(x, Mathf.Sqrt(Mathf.Max(0f, 1f - r * r)), z);
            }
            Picture = made.Keep(new Texture2D(Wide, Deep, TextureFormat.RGBA32, false) { name = "Photo Studio shadow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear });
            var clear = new Color32[Wide * Deep];
            Picture.SetPixels32(clear);
            Picture.Apply(false);
        }

        /// <summary>Go on for as long as the time given (milliseconds). True once everything is worked out (and the picture made).</summary>
        public bool Work(float milliseconds)
        {
            if (Done) return true;
            long until = Stopwatch.GetTimestamp() + (long)(milliseconds * 0.001 * Stopwatch.Frequency);
            while (next < dark.Length)
            {
                int x = next % Wide, z = next / Wide;
                // (from a little under the floor: a line that began inside a part standing on the floor would not meet it)
                Vector3 from = origin + frame * new Vector3(Low.x + (x + 0.5f) * Size.x / Wide, floor - Under, Low.y + (z + 0.5f) * Size.y / Deep);
                float sum = 0f;
                for (int i = 0; i < Rays; i++)
                {
                    float met = Meets(from, frame * directions[i]);
                    if (met < 0f) continue;
                    // (what is near shades more than what is far: the far parts let the room's light in round them)
                    float near = 1f - Mathf.Max(0f, met - Under) / reach;
                    sum += near * near;
                }
                dark[next] = sum / Rays;
                next++;
                if ((next & 7) == 0 && Stopwatch.GetTimestamp() > until) return false;
            }
            Finish();
            return true;
        }

        /// <summary>How far along a line the nearest part is that casts a shadow; less than nought for none.</summary>
        float Meets(Vector3 from, Vector3 way)
        {
            const int Parts = 1 << 0;
            if (ignored == null)
                return Physics.Raycast(from, way, out RaycastHit hit, reach + Under, Parts, QueryTriggerInteraction.Ignore) ? hit.distance : -1f;
            int n = Physics.RaycastNonAlloc(from, way, hits, reach + Under, Parts, QueryTriggerInteraction.Ignore);
            float nearest = -1f;
            for (int k = 0; k < n; k++)
                if (!ignored.Contains(hits[k].collider) && (nearest < 0f || hits[k].distance < nearest)) nearest = hits[k].distance;
            return nearest;
        }

        void Finish()
        {
            // Smoothed twice over its neighbours (the lines are few for each spot), then made a picture: black, as see-through as it is light.
            float[] a = dark, b = new float[dark.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int z = 0; z < Deep; z++)
                    for (int x = 0; x < Wide; x++)
                    {
                        float sum = 0f, weight = 0f;
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, zz = z + dz;
                                if (xx < 0 || zz < 0 || xx >= Wide || zz >= Deep) continue;
                                float w = (dx == 0 ? 2f : 1f) * (dz == 0 ? 2f : 1f);
                                sum += a[zz * Wide + xx] * w; weight += w;
                            }
                        b[z * Wide + x] = sum / weight;
                    }
                float[] t = a; a = b; b = t;
            }
            var pixels = new Color32[dark.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                int x = i % Wide, z = i / Wide;
                // (nothing at the patch's own edge, so that it never shows as a square)
                float edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(x, Wide - 1 - x), Mathf.Min(z, Deep - 1 - z)) / 3f);
                float value = (1f - Mathf.Exp(-Stage.K("shade", 9f) * a[i])) * edge;
                pixels[i] = new Color32(0, 0, 0, (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255));
            }
            Picture.SetPixels32(pixels);
            Picture.Apply(false);
            Done = true;
        }

#if DEV
        /// <summary>(For the development build: what the shadow came to, and what a line straight up from under the middle of the craft meets.)</summary>
        public string Report()
        {
            float most = 0f, sum = 0f;
            foreach (float d in dark) { most = Mathf.Max(most, d); sum += d; }
            Vector3 middle = origin + frame * new Vector3(Low.x + 0.5f * Size.x, floor - Under, Low.y + 0.5f * Size.y);
            string seen = "";
            foreach (RaycastHit hit in Physics.RaycastAll(middle, frame * Vector3.up, 60f, ~0, QueryTriggerInteraction.Collide))
                seen += " | " + hit.collider.name + " layer " + hit.collider.gameObject.layer + (hit.collider.isTrigger ? " trigger" : "") + (ignored != null && ignored.Contains(hit.collider) ? " ignored" : "") + " at " + hit.distance.ToString("F2") + " enabled " + hit.collider.enabled;
            return (Done ? "done" : "working " + Progress.ToString("P0")) + ", " + Wide + " x " + Deep + " over " + Size.ToString("F1") + ", reach " + reach.ToString("F1") + ", darkest " + most.ToString("F3") + ", mean " + (sum / Mathf.Max(1, dark.Length)).ToString("F4")
                   + ", ignored " + (ignored != null ? ignored.Count : 0) + "; up from under the middle:" + (seen.Length > 0 ? seen : " nothing");
        }
#endif

        /// <summary>How much of the work is done, 0 to 1.</summary>
        public float Progress => dark.Length == 0 ? 1f : next / (float)dark.Length;
    }
}
