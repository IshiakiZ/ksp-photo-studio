using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PhotoStudio
{
    /// <summary>
    /// Anything in the game looked at alone, where it stands: a building, a tower, one lamp, a craft, a cloud, an explosion,
    /// a planet in the sky. Asked for on 2026-10-11 ("bring any part of the game into the studio ... clouds, a planet, one
    /// specific spotlight, a tower, a building, anything"; "this feature is for you mostly", that is for whoever is making the
    /// mods and has to judge one thing without the rest of the scene round it, "and photo displays of stuff").
    ///
    /// The craft's studio (Stage) builds a set round a craft and draws it with a camera of its own. This does the opposite, so
    /// that it works for things no set can hold (a cloud a kilometre across, smoke, a flame: whatever the game's own cameras
    /// draw): the game's cameras go on drawing, and the rest of the world is taken away from them:
    ///
    ///   Alone      one thing by its name and nothing else solid; "air": no ground or buildings (the craft stays); "nothing":
    ///              nor the craft: only what is in the air (clouds, smoke, flames)
    ///   Sky        a plain backdrop in place of the sky (grey, black, white, blue, or r,g,b): the stars, the planet's own
    ///              disc and the air's colour go
    ///   Still      the game's clock stopped (the picture goes on being drawn), so that one moment can be looked at from
    ///              several sides
    ///   Find       what there is to name: things with something to draw, by a word in their names, nearest first
    ///   Back       everything as it was (also done by itself when the scene changes)
    ///
    /// Everything it changes is written down as it was and put back from that.
    /// </summary>
    public static class Exhibit
    {
        const int Scenery = 15, Parts = 0, Kerbals = 17, Water = 4;

        sealed class Eye { public Camera cam; public int mask; public CameraClearFlags clear; public Color back; public bool on; }
        static readonly List<Eye> eyes = new List<Eye>();
        static readonly List<Renderer> hidden = new List<Renderer>();
        static readonly List<KeyValuePair<GameObject, int>> moved = new List<KeyValuePair<GameObject, int>>();
        static Transform only;
        static bool hideParts, hooked, seaToo;
        static float wasScale = -1f, nextSweep;
        static Sweeper sweeper;

        /// <summary>(Things come into being while one is looked at alone: ground is made as the camera moves. They are hidden as they come.)</summary>
        sealed class Sweeper : MonoBehaviour
        {
            void LateUpdate()
            {
                if (only == null && !hideParts && !seaToo) return;
                if (Time.unscaledTime < nextSweep) return;
                nextSweep = Time.unscaledTime + 0.5f;
                HideOthers();
            }
        }

        static void Remember()
        {
            if (eyes.Count > 0) return;
            foreach (Camera c in Camera.allCameras)
                eyes.Add(new Eye { cam = c, mask = c.cullingMask, clear = c.clearFlags, back = c.backgroundColor, on = c.enabled });
            if (!hooked) { GameEvents.onGameSceneLoadRequested.Add(scene => Back()); hooked = true; }
        }

        static bool Local(Camera c) => c != null && c.name.StartsWith("Camera 0", StringComparison.Ordinal);

        static bool Under(Transform t, Transform root)
        {
            for (; t != null; t = t.parent) if (t == root) return true;
            return false;
        }

        static void HideOthers()
        {
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                int layer = r.gameObject.layer;
                // (the sea, which shows where the ground over it has been taken away: the game's own, and Scatterer's)
                Material paint = r.sharedMaterial;
                if (layer == Water || (paint != null && paint.shader != null && paint.shader.name.IndexOf("Ocean", StringComparison.OrdinalIgnoreCase) >= 0)) { r.enabled = false; hidden.Add(r); continue; }
                bool solid = hideParts && (layer == Scenery || layer == Parts || layer == Kerbals);
                if (only != null) solid = layer == Parts || layer == Kerbals;
                if (!solid) continue;
                if (only != null && Under(r.transform, only)) continue;
                // (what draws smoke, flames and clouds is in the air, not solid, whatever layer it is on: it is known by its shader)
                Material m = r.sharedMaterial;
                if (m != null && m.shader != null && (m.shader.name.IndexOf("Volum", StringComparison.OrdinalIgnoreCase) >= 0 || m.shader.name.IndexOf("Cloud", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                r.enabled = false;
                hidden.Add(r);
            }
        }

        static void ShowAgain()
        {
            foreach (Renderer r in hidden) if (r != null) r.enabled = true;
            hidden.Clear();
            foreach (KeyValuePair<GameObject, int> m in moved) if (m.Key != null) m.Key.layer = m.Value;
            moved.Clear();
        }

        /// <summary>What there is to name: things with something to draw whose names hold the word, nearest the camera first.</summary>
        public static string Find(string word)
        {
            word = (word ?? "").Trim();
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : Camera.main;
            Vector3 from = cam != null ? cam.transform.position : Vector3.zero;
            var seen = new Dictionary<Transform, Bounds>();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                // (the thing is the highest of its parents whose name holds the word; with no word, the top of its tree)
                Transform named = null;
                for (Transform t = r.transform; t != null; t = t.parent)
                    if (word.Length == 0 ? t.parent == null : t.name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) named = t;
                if (named == null) continue;
                if (seen.TryGetValue(named, out Bounds b)) { b.Encapsulate(r.bounds); seen[named] = b; }
                else seen[named] = r.bounds;
            }
            var all = new List<KeyValuePair<Transform, Bounds>>(seen);
            all.Sort((a, b) => (a.Value.center - from).sqrMagnitude.CompareTo((b.Value.center - from).sqrMagnitude));
            var s = new StringBuilder(all.Count + " things" + (word.Length > 0 ? " named like '" + word + "'" : "") + ", nearest first:\n");
            // (and where each is from the craft being flown, east, north and up: what the recorder's camera is told to look at)
            Vessel v = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            Vector3d up = Vector3d.up, east = Vector3d.right, north = Vector3d.forward;
            if (v != null) { up = FlightGlobals.getUpAxis(v.mainBody, v.CoMD); east = Vector3d.Cross(up, v.mainBody.transform.up).normalized; north = Vector3d.Cross(east, up); }
            for (int n = 0; n < all.Count && n < 24; n++)
            {
                Bounds b = all[n].Value;
                s.Append(all[n].Key.name).Append(" | ");
                if (v != null)
                {
                    Vector3d d = (Vector3d)b.center - v.CoMD;
                    s.Append("east ").Append(Vector3d.Dot(d, east).ToString("F0")).Append(" north ").Append(Vector3d.Dot(d, north).ToString("F0")).Append(" up ").Append(Vector3d.Dot(d, up).ToString("F0")).Append(" | ");
                }
                s.Append((b.center - from).magnitude.ToString("F0")).Append(" m off | ")
                 .Append(b.size.x.ToString("F0")).Append(" x ").Append(b.size.y.ToString("F0")).Append(" x ").Append(b.size.z.ToString("F0")).Append(" m | layer ").Append(all[n].Key.gameObject.layer).Append('\n');
            }
            return s.ToString();
        }

        /// <summary>One thing alone ("air": no ground or buildings; "nothing": nor craft; a name: that thing and nothing else solid; nothing said: as it was).</summary>
        public static string Alone(string what)
        {
            what = (what ?? "").Trim();
            Remember();
            ShowAgain();
            only = null; hideParts = false; seaToo = false;
            foreach (Eye e in eyes) if (e.cam != null && Local(e.cam)) e.cam.cullingMask = e.mask;
            if (what.Length == 0) return "everything shown";
            if (sweeper == null) { var go = new GameObject("PhotoStudio exhibit"); UnityEngine.Object.DontDestroyOnLoad(go); sweeper = go.AddComponent<Sweeper>(); }
            if (what == "air" || what == "nothing")
            {
                foreach (Eye e in eyes) if (e.cam != null && Local(e.cam)) e.cam.cullingMask = e.mask & ~(1 << Scenery) & ~(1 << Water);
                hideParts = what == "nothing";
                seaToo = true;
                HideOthers();
                return what == "air" ? "the ground and the buildings taken away" : "everything solid taken away";
            }
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : Camera.main;
            Vector3 from = cam != null ? cam.transform.position : Vector3.zero;
            Transform best = null; float nearest = float.MaxValue;
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                Transform named = null;
                for (Transform t = r.transform; t != null; t = t.parent) if (t.name.IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0) named = t;
                if (named == null) continue;
                float d = (r.bounds.center - from).sqrMagnitude;
                if (d < nearest) { nearest = d; best = named; }
            }
            if (best == null) return "nothing here is named like '" + what + "' (see Find)";
            only = best;
            // (The ground, the buildings and the sea are taken from the cameras as one, by their layer: Scatterer's sea is no thing
            // that can be switched off by itself. The thing looked at, if it is scenery itself, is put on the craft's layer
            // meanwhile, which the cameras still draw and the sun still lights.)
            foreach (Renderer r in best.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.layer == Scenery) { moved.Add(new KeyValuePair<GameObject, int>(r.gameObject, Scenery)); r.gameObject.layer = Parts; }
            foreach (Eye e in eyes) if (e.cam != null && Local(e.cam)) e.cam.cullingMask = e.mask & ~(1 << Scenery) & ~(1 << Water);
            HideOthers();
            return best.name + " alone, " + Mathf.Sqrt(nearest).ToString("F0") + " m off; " + hidden.Count + " other things hidden";
        }

        /// <summary>A plain backdrop in place of the sky: grey, black, white, blue, or "r,g,b" (0 to 1); nothing said: the sky again.</summary>
        public static string Sky(string colour)
        {
            colour = (colour ?? "").Trim().ToLowerInvariant();
            Remember();
            foreach (Eye e in eyes)
            {
                if (e.cam == null) continue;
                e.cam.enabled = e.on; e.cam.clearFlags = e.clear; e.cam.backgroundColor = e.back;
            }
            if (colour.Length == 0) return "the sky as it is";
            Color c;
            if (colour == "grey" || colour == "gray") c = new Color(0.5f, 0.5f, 0.5f);
            else if (colour == "black") c = Color.black;
            else if (colour == "white") c = Color.white;
            else if (colour == "blue") c = new Color(0.25f, 0.45f, 0.8f);
            else
            {
                string[] parts = colour.Split(',');
                if (parts.Length < 3) return "a colour is grey, black, white, blue or r,g,b";
                c = new Color(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
            }
            // (the first of the scene's own cameras to draw clears the picture to the colour; what drew the sky before it is switched off)
            Camera first = null;
            foreach (Eye e in eyes) if (e.cam != null && e.on && Local(e.cam) && (first == null || e.cam.depth < first.depth)) first = e.cam;
            if (first == null) return "no camera of the scene's to give a backdrop";
            int off = 0;
            foreach (Eye e in eyes)
                if (e.cam != null && e.on && e.cam.depth < first.depth && e.cam.targetTexture == null) { e.cam.enabled = false; off++; }
            first.clearFlags = CameraClearFlags.SolidColor;
            first.backgroundColor = c;
            return "a backdrop of " + colour + " behind " + first.name + "; " + off + " cameras of the sky switched off";
        }

        /// <summary>The game's clock stopped ("on") or going again: one moment to be looked at from several sides.</summary>
        public static string Still(string on)
        {
            bool stop = (on ?? "").Trim() == "on";
            if (stop && wasScale < 0f) { wasScale = Time.timeScale; Time.timeScale = 0f; }
            else if (!stop && wasScale >= 0f) { Time.timeScale = wasScale; wasScale = -1f; }
            return stop ? "the clock stopped" : "the clock going";
        }

        /// <summary>Everything as it was.</summary>
        public static string Back()
        {
            ShowAgain();
            only = null; hideParts = false; seaToo = false;
            foreach (Eye e in eyes)
            {
                if (e.cam == null) continue;
                e.cam.cullingMask = e.mask; e.cam.clearFlags = e.clear; e.cam.backgroundColor = e.back; e.cam.enabled = e.on;
            }
            eyes.Clear();
            if (wasScale >= 0f) { Time.timeScale = wasScale; wasScale = -1f; }
            return "everything as it was";
        }
    }
}
