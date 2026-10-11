using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace PhotoStudio
{
    /// <summary>
    /// The studio as it stands in the assembly building or the hangar: the craft being built, in a set of the studio's
    /// own round it, lit by the studio's own lights, seen through the editor's own camera moved by the studio. (Or in
    /// flight, round the craft being flown, with the game paused: see BeginFlight.)
    ///
    /// What is done to the game's scene while it is up, all of it written down first and put back exactly when it is
    /// closed (see Ledger):
    ///   * the camera that draws the building is switched off, and the editor's camera no longer draws the ground crew
    ///     and their trucks (nothing of the building or the crew is itself touched);
    ///   * the building's lamps that light the craft are switched off;
    ///   * the sheet that takes the craft's shadow on the building's floor, and launch clamps, are hidden;
    ///   * the editor's camera is moved by the studio instead of by its own driver, and its angle of view is the studio's lens;
    ///   * the light from all round and the reflections in the parts are the studio's;
    ///   * the editor's picking, placing and moving of parts is held off (saving, loading, launching and leaving are not);
    ///   * the game's interface is put away (if that setting is on).
    /// </summary>
    sealed class Stage
    {
        const int Parts = 0, Building = 15, Crew = 16, Eva = 17;
        const string Lock = "PhotoStudio";

        public Camera Eye { get; }
        public Camera Back { get; private set; }
        public int Kind { get; private set; }
        public int SetLayer { get; private set; }
        public bool Shading => shade != null && !shade.Done;

        readonly Ledger scene = new Ledger(), look = new Ledger();
        readonly Made made = new Made();
        Made built = new Made();
        GameObject root;
        Transform set, held;
        Mirror mirror;
        readonly List<Renderer> backdrop = new List<Renderer>();
        Renderer shadowShown;
        Material shadowPaint;
        Shade shade;
        float shadeFade;
        Cubemap seen;
        float seenFrom, seenAgain;                              // the heading the reflections were taken at, and when they may be taken again
        Mesh floor;
        Vector3[] floorAt;
        Color32[] floorColour;
        Color floorBase;
        Material screenPaint;
        bool closed;

        // The studio's own axes: its floor lies across them and its up is theirs. In the editors they are the world's own; they
        // are kept apart so that the studio could stand where the world's axes are not upright (in flight, the planet's up).
        Quaternion frame = Quaternion.identity;
        Vector3 origin;                                         // what the craft's box is measured from, in the world
        Vector3 Up => frame * Vector3.up;

        // the craft: its box (in the studio's axes, from the origin), the spot on the floor under its middle, how wide and how tall it is, and the radius of a ball round it
        Bounds craft;
        Vector3 foot;
        float across, tall, ball;
        float room, roof;                                        // the set's own size (before it is made larger to keep the camera inside)

        // the camera: as asked for, and as shown (which follows smoothly)
        float heading, pitch, distance, height;
        float headingNow, pitchNow, distanceNow, heightNow, scaleNow = 1f;
        float dirtyAt = -1f;

        // in flight: the craft, what moves the flight camera, where the craft was last frame, and when the lamps were last looked over
        public bool InFlight { get; private set; }
        Vessel vessel;
        Transform rig;
        Vector3 vesselAt;
        float sweepAt;

        /// <summary>The craft's parts: the editor's craft, or in flight the craft being flown.</summary>
        List<Part> CraftParts => InFlight ? (vessel != null ? vessel.parts : null) : (EditorLogic.fetch != null && EditorLogic.fetch.ship != null ? EditorLogic.fetch.ship.parts : null);

        Stage(Camera eye) { Eye = eye; }

        /// <summary>The studio put up round the craft in the editor, or in flight; nothing (and why) if it cannot be.</summary>
        public static Stage Open(int kind, out string why)
        {
            if (HighLogic.LoadedSceneIsFlight) return OpenInFlight(kind, out why);
            why = null;
            if (!HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null || EditorLogic.fetch.ship == null) { why = "The studio opens in the assembly building, the hangar, or in flight."; return null; }
            if (EditorLogic.fetch.ship.parts.Count == 0) { why = "There is no craft to photograph yet."; return null; }
            if (EditorLogic.SelectedPart != null) { why = "Put down the part you are holding first."; return null; }
            if (!Shaders.Ready) { why = "This game has none of " + Shaders.Missing + ", which the studio is drawn with."; return null; }
            Camera eye = EditorCamera.Instance != null && EditorCamera.Instance.cam != null ? EditorCamera.Instance.cam : Camera.main;
            if (eye == null) { why = "The editor's camera cannot be found."; return null; }
            var stage = new Stage(eye);
            try { stage.Begin(kind); }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                stage.Close();
                why = "The studio could not be set up (" + ex.Message + "); everything is as it was.";
                return null;
            }
            return stage;
        }

        /// <summary>Whether the Cinema Camera mod (another of Keystone's) is switched on: it moves and sets the flight camera too.</summary>
        public static bool CinemaCameraOn
        {
            get
            {
                Keystone.Mod cine = Keystone.Kit.Find("Cinema Camera");
                if (cine != null)
                    foreach (Keystone.Setting s in cine.Settings)
                        if (s.Key == "on" && s is Keystone.Toggle on) return on.Value;
                return false;
            }
        }

        static Stage OpenInFlight(int kind, out string why)
        {
            why = null;
            Vessel flown = FlightGlobals.ActiveVessel;
            if (flown == null || FlightCamera.fetch == null || FlightCamera.fetch.mainCamera == null) { why = "There is no craft being flown."; return null; }
            if (MapView.MapIsEnabled) { why = "Leave the map first."; return null; }
            if (CameraManager.Instance != null && CameraManager.Instance.currentCameraMode != CameraManager.CameraMode.Flight) { why = "The studio opens from the camera outside the craft."; return null; }
            if (!Shaders.Ready) { why = "This game has none of " + Shaders.Missing + ", which the studio is drawn with."; return null; }
            // (Cinema Camera moves and sets the same camera: one at a time)
            if (CinemaCameraOn) { why = "Switch Cinema Camera off first: both would move the camera."; return null; }
            var stage = new Stage(FlightCamera.fetch.mainCamera) { InFlight = true, vessel = flown, rig = FlightCamera.fetch.transform };
            try { stage.BeginFlight(kind); }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                stage.Close();
                why = "The studio could not be set up (" + ex.Message + "); everything is as it was.";
                return null;
            }
            return stage;
        }

        /// <summary>
        /// In flight: the game is paused (as a game's photo mode pauses it), so that the craft stands still; the planet, its
        /// sky and everything but the craft go out of sight (the cameras that draw the sky, the planet and the distance are
        /// switched off, the near camera draws only craft, kerbals and the set, other craft are hidden); the sun and every
        /// other lamp on the craft go out (the studio brings its own); the flight camera is held and moved by the studio.
        /// The studio's floor lies level under the craft, across the planet's up where the craft is.
        /// </summary>
        void BeginFlight(int kind)
        {
            Kind = Mathf.Clamp(kind, 0, 2);
            Vector3 up = ((Vector3)FlightGlobals.getUpAxis(vessel.mainBody, vessel.CoMD)).normalized;
            Vector3 along = Vector3.ProjectOnPlane(Eye.transform.forward, up);
            if (along.sqrMagnitude < 1e-4f) along = Vector3.ProjectOnPlane(vessel.mainBody.transform.up, up);
            if (along.sqrMagnitude < 1e-4f) along = Vector3.ProjectOnPlane(Vector3.forward, up);
            frame = Quaternion.LookRotation(along.normalized, up);
            origin = vesselAt = vessel.transform.position;
            if (!Measure()) throw new Exception("nothing of the craft can be seen");

            // ---- the game paused first (pausing brings the interface back, which is put away again below)
            if (!FlightDriver.Pause)
            {
                FlightDriver.SetPause(true, false);
                scene.Add(() => { if (FlightDriver.Pause && HighLogic.LoadedSceneIsFlight) FlightDriver.SetPause(false, false); });
            }

            // ---- the flight camera: held, and moved by the studio until it is closed
            FlightCamera driver = FlightCamera.fetch;
            scene.Place(rig);
            scene.Place(Eye.transform);
            scene.Lens(Eye);
            if (driver.updateActive)
            {
                driver.DeactivateUpdate();
                scene.Add(() => { if (FlightCamera.fetch != null) FlightCamera.fetch.ActivateUpdate(); });
            }
            foreach (Highlighting.HighlightingSystem glow in Eye.GetComponentsInChildren<Highlighting.HighlightingSystem>()) scene.Enable(glow, false);

            // ---- the world out of sight: the cameras that draw before the near one (the sky, the planet, the distance) go dark,
            // and the near one draws craft and kerbals and the set alone
            foreach (Camera c in Camera.allCameras)
                if (c != Eye && c.targetTexture == null && c.depth < Eye.depth) scene.Enable(c, false);
            SetLayer = PickLayer(Eye);
            scene.Mask(Eye, (1 << Parts) | (1 << 1) | (1 << Eva) | (1 << SetLayer));
            // (other craft near by, the craft's own launch clamps)
            foreach (Vessel other in FlightGlobals.VesselsLoaded)
                if (other != null && other != vessel)
                    foreach (Part p in other.parts)
                        if (p != null) foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true)) scene.Hide(r);
            HideClamps();

            Common();
        }

        void Begin(int kind)
        {
            Kind = Mathf.Clamp(kind, 0, 2);
            if (!Measure()) throw new Exception("nothing of the craft can be seen");

            // ---- the editor's camera: its own driver stops; the studio moves it until it is closed
            scene.Place(Eye.transform);
            scene.Lens(Eye);
            if (EditorDriver.fetch != null)
            {
                scene.Enable(EditorDriver.fetch.vabCamera, false);
                scene.Enable(EditorDriver.fetch.sphCamera, false);
            }
            // (The editor's camera looks a little to one side of where it points, to leave room for the parts list at the left of
            // the screen: its picture is made off centre every frame. The studio's pictures are centred.)
            foreach (Behaviour b in Eye.GetComponents<Behaviour>())
            {
                string what = b.GetType().Name;
                if (what == "CameraOffCenter" || what == "EditorCameraOffset") scene.Enable(b, false);
            }
            scene.Add(() => { if (Eye != null) Eye.ResetProjectionMatrix(); });
            Eye.ResetProjectionMatrix();
            // (the glow the editor puts round a part under the pointer is the editor's, not the craft's: none in the studio or its pictures)
            if (EditorCamera.Instance != null) scene.Enable(EditorCamera.Instance.highLightingSystem, false);

            // ---- the building: whatever camera draws it before the editor's own goes dark; the crew and their trucks are not drawn.
            // And the editor's other cameras that ride on its own (the one that draws the markers of the centres of mass and lift)
            foreach (Camera c in Camera.allCameras)
                if (c != Eye && c.targetTexture == null && ((c.depth <= Eye.depth && (c.cullingMask & (1 << Building)) != 0) || c.transform.IsChildOf(Eye.transform))) scene.Enable(c, false);
            // (the editor's camera clears nothing, as the game has it: the building's camera cleared the picture for it. The studio's
            // backdrop camera clears it now, and in the showroom draws the reflection first, whose depth must not hide the room)
            CameraClearFlags clearing = Eye.clearFlags;
            scene.Add(() => { if (Eye != null) Eye.clearFlags = clearing; });
            Eye.clearFlags = CameraClearFlags.Depth;
            SetLayer = PickLayer(Eye);
            scene.Mask(Eye, (Eye.cullingMask & ~((1 << Building) | (1 << Crew))) | (1 << SetLayer));

            // ---- what lies on the parts' own layer but belongs to the building: the unseen sheet that takes the craft's shadow on its floor
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                Material m = r.sharedMaterial;
                if (m != null && m.shader != null && m.shader.name.IndexOf("Shadow Receiver", StringComparison.OrdinalIgnoreCase) >= 0) scene.Hide(r);
            }
            HideClamps();

            // ---- the editor's own handling of parts is held off: nothing is picked up or moved by a click meant for the camera.
            // (And the game's menu on Escape: Escape closes the studio instead; see Studio.)
            InputLockManager.SetControlLock(ControlTypes.EDITOR_SOFT_LOCK | ControlTypes.PAUSE, Lock);
            scene.Add(() => InputLockManager.RemoveControlLock(Lock));
            Common();
        }

        /// <summary>What is the same in the editor and in flight: the lamps, the light from all round, the interface, and the studio's own things.</summary>
        void Common()
        {
            if (InFlight)
            {
                // (in flight everything that would move the craft or the time is held off while it is paused; the camera is the studio's)
                InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS | ControlTypes.CAMERAMODES, Lock);
                scene.Add(() => InputLockManager.RemoveControlLock(Lock));
            }

            // ---- the lamps that light the craft go out (the studio brings its own), and are looked over again while it is up
            Sweep();
            NoGlow();

            // ---- the light from all round, the reflections and the fog (the studio's own are set for each studio); the shadows' reach
            scene.Surroundings();
            RenderSettings.fog = false;
            float reach = QualitySettings.shadowDistance;
            scene.Add(() => QualitySettings.shadowDistance = reach);

            // ---- the game's interface, put away (and back at the end, if it was showing)
            if (Studio.HideInterface && KSP.UI.UIMasterController.Instance != null && KSP.UI.UIMasterController.Instance.IsUIShowing)
            {
                GameEvents.onHideUI.Fire();
                scene.Add(() => { if (KSP.UI.UIMasterController.Instance != null && !KSP.UI.UIMasterController.Instance.IsUIShowing) GameEvents.onShowUI.Fire(); });
            }

            // ---- the studio's own: everything under one object, which goes when the studio is closed
            root = new GameObject("Photo Studio");
            var backHolder = new GameObject("Photo Studio backdrop camera");
            backHolder.transform.SetParent(root.transform, false);
            Back = backHolder.AddComponent<Camera>();
            Back.depth = Eye.depth - 0.5f;
            Back.clearFlags = CameraClearFlags.SolidColor;
            Back.cullingMask = 0;
            Back.allowHDR = Eye.allowHDR;
            Back.allowMSAA = Eye.allowMSAA;
            Back.useOcclusionCulling = false;
            Back.renderingPath = RenderingPath.Forward;
            Back.nearClipPlane = Eye.nearClipPlane;
            mirror = backHolder.AddComponent<Mirror>();
            mirror.Eye = Eye;

            // ---- the camera begins where the game's was, looking at the craft from the same side
            Vector3 middle = foot + Up * (0.5f * tall);
            Vector3 view = Quaternion.Inverse(frame) * (middle - Eye.transform.position);
            heading = view.sqrMagnitude > 0.01f ? Mathf.Atan2(view.x, view.z) * Mathf.Rad2Deg : 0f;
            Build(Kind);
            Frame(true);
            headingNow = heading; pitchNow = pitch; distanceNow = distance; heightNow = height;
            scaleNow = ScaleFor(distanceNow, pitchNow, heightNow);
            // (the light from all round, set again just before the studio's two cameras draw: in flight another mod sets its own every frame)
            Camera.onPreCull += BeforeDrawing;
            hooked = true;
            Place(0f);
        }

        bool hooked;

        void BeforeDrawing(Camera cam)
        {
            if (closed || cam == null || (cam != Eye && cam != Back) || !haveProbe) return;
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = probeMade;
            if (seen != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflection = seen;
                RenderSettings.reflectionIntensity = 1f;
            }
            RenderSettings.fog = false;
        }

        /// <summary>Every lamp that would light the craft but the studio's own, out (written down the first time, put back at the end).</summary>
        void Sweep()
        {
            int lit = (1 << Parts) | (1 << Eva) | (1 << SetLayer);
            foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>())
                if (l != null && l.enabled && (l.cullingMask & lit) != 0 && (root == null || !l.transform.IsChildOf(root.transform))) scene.Enable(l, false);
            sweepAt = Time.unscaledTime + 0.5f;
        }

        /// <summary>
        /// The glow the game puts on a part under the pointer (in the editor, on everything that hangs from it as well) held off: it
        /// is the game's, not the craft's. The game lays it into the parts' own materials, not only round them (that is the
        /// camera's highlighting, switched off above): so each part is told to take none until the studio is closed. Done every
        /// frame, before the frame is drawn: the editor puts the glow on at the end of a frame, so none put on while the studio
        /// was opening is ever seen.
        /// </summary>
        void NoGlow()
        {
            List<Part> parts = CraftParts;
            if (parts == null) return;
            foreach (Part p in parts) scene.NoGlow(p);
        }

        /// <summary>
        /// A layer for the set: one that no camera of the game's draws but the editor's own (once the building's is off),
        /// and that nothing of the game's is on, so that the set can be shown, hidden and looked at by itself. The parts'
        /// own layer if there is none.
        /// </summary>
        static int PickLayer(Camera eye)
        {
            int[] wanted = { 3, 6, 7, 30, 29, 25, 22, 14, 13 };
            var used = new bool[32];
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>()) used[r.gameObject.layer] = true;
            foreach (Camera c in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                if (c == eye || !c.enabled) continue;
                for (int i = 0; i < 32; i++) if ((c.cullingMask & (1 << i)) != 0) used[i] = true;
            }
            foreach (int l in wanted) if (!used[l] && string.IsNullOrEmpty(LayerMask.LayerToName(l))) return l;
            foreach (int l in wanted) if (!used[l]) return l;
            return Parts;
        }

        /// <summary>
        /// The craft's box, in the studio's axes, from what is drawn of its parts: each mesh's own box, its corners carried
        /// into the studio's axes (tighter than the world's boxes round turned parts). Launch clamps are left out: they are
        /// hidden in the studio.
        /// </summary>
        bool Measure()
        {
            List<Part> parts = CraftParts;
            if (parts == null) return false;
            bool any = false;
            Bounds all = default;
            Quaternion back = Quaternion.Inverse(frame);
            foreach (Part p in parts)
            {
                if (p == null || p.Modules.GetModule<LaunchClamp>() != null) continue;
                foreach (Renderer r in p.FindModelRenderersCached())
                {
                    if (r == null || !r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy) continue;
                    if (p.partRendererBoundsIgnore != null && p.partRendererBoundsIgnore.Contains(r.name)) continue;
                    Bounds own;
                    Matrix4x4 toWorld;
                    if (r is SkinnedMeshRenderer skinned) { own = skinned.localBounds; toWorld = skinned.rootBone != null ? skinned.rootBone.localToWorldMatrix : skinned.transform.localToWorldMatrix; }
                    else if (r is MeshRenderer)
                    {
                        MeshFilter shape = r.GetComponent<MeshFilter>();
                        if (shape == null || shape.sharedMesh == null) continue;
                        own = shape.sharedMesh.bounds;
                        toWorld = r.transform.localToWorldMatrix;
                    }
                    else continue;
                    if (own.size.sqrMagnitude < 1e-8f) continue;
                    for (int c = 0; c < 8; c++)
                    {
                        Vector3 corner = own.center + Vector3.Scale(own.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                        Vector3 framed = back * (toWorld.MultiplyPoint3x4(corner) - origin);
                        if (!any) { all = new Bounds(framed, Vector3.zero); any = true; }
                        else all.Encapsulate(framed);
                    }
                }
            }
            if (!any) return false;
            craft = all;
            foot = origin + frame * new Vector3(all.center.x, all.min.y, all.center.z);
            across = Mathf.Max(all.size.x, all.size.z);
            tall = all.size.y;
            ball = Mathf.Max(0.5f, all.extents.magnitude);
            room = Mathf.Max(14f, 3.2f * ball);
            return true;
        }

        void HideClamps()
        {
            List<Part> parts = CraftParts;
            if (parts == null) return;
            foreach (Part p in parts)
                if (p != null && p.Modules.GetModule<LaunchClamp>() != null)
                    foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true)) scene.Hide(r);
        }

        // ------------------------------------------------------------------------------------------------ the sets

        /// <summary>Put up one of the studios (taking down the one that was up).</summary>
        public void Build(int kind)
        {
            look.Restore();
            // (out of sight at once: what is destroyed goes only at the end of the frame, and the new set's reflections are taken now)
            if (set != null) { set.gameObject.SetActive(false); UnityEngine.Object.Destroy(set.gameObject); }
            if (held != null) { held.gameObject.SetActive(false); UnityEngine.Object.Destroy(held.gameObject); }
            built.Destroy();
            built = new Made();
            backdrop.Clear();
            shadowShown = null; shadowPaint = null; shade = null; floor = null; screenPaint = null;
            Kind = Mathf.Clamp(kind, 0, 2);

            set = new GameObject("Photo Studio set").transform;
            set.SetParent(root.transform, false);
            set.SetPositionAndRotation(foot, frame * Quaternion.Euler(0f, headingNow, 0f));
            set.localScale = Vector3.one * scaleNow;
            held = new GameObject("Photo Studio by the craft").transform;
            held.SetParent(root.transform, false);
            held.SetPositionAndRotation(foot, frame);
            mirror.On = false;
            Back.cullingMask = 0;

            switch (Kind)
            {
                case 0: White(); break;
                case 1: Green(); break;
                default: Showroom(); break;
            }
            Reflections();
        }

        /// <summary>A light of the studio's, for the craft only. `from` is where it shines from, seen from the craft, with +z away from the camera and +y up.</summary>
        Light Lamp(string name, Vector3 from, float intensity, bool shadows)
        {
            var thing = new GameObject("Photo Studio " + name);
            thing.transform.SetParent(set, false);
            Vector3 going = -Side(from).normalized;
            thing.transform.localRotation = Quaternion.LookRotation(going, Mathf.Abs(going.y) > 0.95f ? Vector3.forward : Vector3.up);
            var lamp = thing.AddComponent<Light>();
            lamp.type = LightType.Directional;
            lamp.color = new Color(1f, 0.985f, 0.96f);
            lamp.intensity = intensity;
            lamp.cullingMask = 1 << Parts;
            lamp.shadows = shadows && QualitySettings.shadows != ShadowQuality.Disable ? LightShadows.Soft : LightShadows.None;
            lamp.shadowStrength = 0.75f;
            lamp.renderMode = LightRenderMode.ForcePixel;
            return lamp;
        }

        /// <summary>
        /// Every number of the studios' lighting, by name, where the development build has changed it while the game runs
        /// (see Studio.Tune); otherwise each is what it was made as.
        /// </summary>
        public static readonly Dictionary<string, float> Knobs = new Dictionary<string, float>();
        internal static float K(string name, float made) => Knobs.TryGetValue(name, out float value) ? value : made;

        // The light from all round, as the studio's: some from everywhere, more from above and from below (the floor), and broad
        // lobes from where the large lights are, which turn with them. (The game's shaders take it as a spherical-harmonics probe:
        // what the light from a large soft source looks like on a surface, which no lamp of the game's can give.)
        float allRound, fromAbove, fromBelow;
        readonly List<KeyValuePair<Vector3, float>> lobes = new List<KeyValuePair<Vector3, float>>();
        float probedAt = float.NaN;

        void Surround(float all, float above, float below, params KeyValuePair<Vector3, float>[] broad)
        {
            allRound = all; fromAbove = above; fromBelow = below;
            lobes.Clear();
            lobes.AddRange(broad);
            probedAt = float.NaN;
            Probe();
        }

        static KeyValuePair<Vector3, float> Lobe(Vector3 from, float strength) => new KeyValuePair<Vector3, float>(Side(from).normalized, strength);

        /// <summary>A direction of the lights' as laid out with the key light on the left, turned over to the right if the key is to be on the right.</summary>
        static Vector3 Side(Vector3 from) => Studio.KeyOnRight ? new Vector3(-from.x, from.y, from.z) : from;

        /// <summary>The probe made again for the way the lights face now (and only when they have turned).</summary>
        void Probe()
        {
            if (!float.IsNaN(probedAt) && Mathf.Abs(Mathf.DeltaAngle(probedAt, headingNow)) < 0.5f) return;
            probedAt = headingNow;
            Quaternion turn = frame * Quaternion.Euler(0f, headingNow, 0f);
            var probe = new SphericalHarmonicsL2();
            // (the probe is light as it is, not as it is shown: the shown values here are made linear first)
            probe.AddAmbientLight(Grey(allRound));
            if (fromAbove > 0f) probe.AddDirectionalLight(Up, Grey(fromAbove), 1f);
            if (fromBelow > 0f) probe.AddDirectionalLight(-Up, Grey(fromBelow), 1f);
            foreach (KeyValuePair<Vector3, float> lobe in lobes)
                if (lobe.Value > 0f) probe.AddDirectionalLight(turn * lobe.Key, Grey(lobe.Value), 1f);
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = probe;
            probeMade = probe;
            haveProbe = true;
        }

        SphericalHarmonicsL2 probeMade;
        bool haveProbe;

        static Color Grey(float shown) => new Color(shown, shown, shown).linear;

        /// <summary>
        /// The white cyclorama: a floor that curves up seamlessly into a wall all round, evenly white with no horizon
        /// anywhere, as product photographers light it (the backdrop as bright as white can be); the craft lit by a large
        /// soft key from the front and above on one side, a fill from the other, a light from overhead and a little from
        /// behind; and under it the soft shadow that the large lights leave.
        /// </summary>
        void White()
        {
            float level = Studio.WhiteLevel;
            Color white = new Color(level, level, level, 1f);
            Mesh cove = Workshop.Turned(built, Workshop.CoveLine(0f, 0.62f * room, 0.38f * room, 1.3f * room, 0f, 10, 16), 128, "white cove");
            Material paint = Workshop.Flat(built, Workshop.Solid(built, Color.white), white, "white");
            backdrop.Add(Workshop.Piece("white cove", set, cove, paint, SetLayer).GetComponent<Renderer>());
            Back.backgroundColor = white;
            Shadow(Studio.ShadowStrength);
            var key = new Vector3(-0.55f, 0.62f, -0.56f);
            Lamp("key", key, K("w.key", 1.0f), true);
            Lamp("fill", new Vector3(0.8f, 0.3f, -0.52f), K("w.fill", 0.18f), false);
            Lamp("top", new Vector3(0f, 1f, 0.15f), K("w.top", 0.2f), false);
            Lamp("rim left", new Vector3(-0.6f, 0.45f, 0.66f), K("w.rim", 0.18f), false);
            Lamp("rim right", new Vector3(0.6f, 0.45f, 0.66f), K("w.rim", 0.18f), false);
            // (a white room: light from everywhere, from the white floor below, and broadly from the big softbox of the key)
            Surround(K("w.all", 0.34f), K("w.above", 0.2f), K("w.below", 0.3f), Lobe(key, K("w.lobe", 0.22f)));
            // (the game's post-processing would make a white backdrop glow over the craft's edges, and where it can measure
            // the picture (on Direct3D) turn the white down to grey: held off here as for the green screen)
            if (!ProcessWhite) Unprocessed();
        }

        /// <summary>(Whether the white studio keeps the game's post-processing: no, unless a test says otherwise.)</summary>
        public static bool ProcessWhite;

        /// <summary>
        /// The green screen: floor and wall one even chroma green with nothing on them (no shadow, no light falling off),
        /// so that the picture can be keyed. The craft is lit by lights of its own, a key, a fill and two strong lights
        /// from behind that part it from the green; nothing green lights it or is reflected in it, and the game's
        /// post-processing (which would darken the corners, glow and grade the green) is held off.
        /// </summary>
        void Green()
        {
            Color green = Studio.ScreenColour;
            green.a = 1f;
            Mesh cove = Workshop.Turned(built, Workshop.CoveLine(0f, 0.62f * room, 0.38f * room, 1.3f * room, 0f, 10, 16), 128, "green cove");
            screenPaint = Workshop.Flat(built, Workshop.Solid(built, Color.white), green, "green");
            backdrop.Add(Workshop.Piece("green cove", set, cove, screenPaint, SetLayer).GetComponent<Renderer>());
            Back.backgroundColor = green;
            var key = new Vector3(-0.55f, 0.62f, -0.56f);
            Lamp("key", key, K("g.key", 0.9f), true);
            Lamp("fill", new Vector3(0.8f, 0.3f, -0.52f), K("g.fill", 0.3f), false);
            Lamp("top", new Vector3(0f, 1f, 0.1f), K("g.top", 0.15f), false);
            Lamp("rim left", new Vector3(-0.62f, 0.42f, 0.66f), K("g.rim", 0.65f), false);
            Lamp("rim right", new Vector3(0.62f, 0.42f, 0.66f), K("g.rim", 0.65f), false);
            // (neutral light from all round: nothing of the green comes back onto the craft)
            Surround(K("g.all", 0.3f), K("g.above", 0.12f), K("g.below", 0.04f), Lobe(key, K("g.lobe", 0.18f)));
            Unprocessed();
        }

        /// <summary>The game's post-processing (TUFX, where installed) held off while this studio is up.</summary>
        void Unprocessed()
        {
            if (processing == null)
            {
                var found = new List<Behaviour>();
                foreach (Behaviour b in Eye.GetComponents<Behaviour>())
                    if (b != null && b.GetType().Name == "PostProcessLayer") found.Add(b);
                processing = found.ToArray();
            }
            foreach (Behaviour b in processing) if (b != null && b.enabled) look.Enable(b, false);
        }

        Behaviour[] processing;

        /// <summary>
        /// The showroom: a dark room with a floor polished like lacquer, in which the craft is reflected (more at a glancing
        /// angle than looking down, as a real floor reflects); a large softbox overhead, two tall strip lights behind on
        /// stands, which are the light from behind that draws the craft's outline; and a ring of light in the floor round
        /// the craft, where a turntable's edge would be.
        /// </summary>
        void Showroom()
        {
            float bend = 0.2f * room, flat = room - bend;
            roof = Mathf.Max(0.55f * room, (1.35f * tall + 2f));
            List<Vector2> line = Workshop.CoveLine(flat, flat, bend, roof, roof, 0, 12);
            // (the colours up the room's line: dark where it leaves the floor, a softly lit band along the wall, darker up into the ceiling)
            float total = 0f;
            for (int i = 1; i < line.Count; i++) total += Vector2.Distance(line[i], line[i - 1]);
            float coveTop = 0.5f * Mathf.PI * bend / total, wallTop = coveTop + (roof - bend) / total;
            Texture2D ramp = Workshop.Ramp(built, new[] { 0f, coveTop, coveTop + 0.45f * (wallTop - coveTop), wallTop, 1f },
                new[] { new Color(0.03f, 0.031f, 0.034f) * K("s.walls", 2.2f), new Color(0.05f, 0.052f, 0.056f) * K("s.walls", 2.2f), new Color(0.105f, 0.108f, 0.115f) * K("s.walls", 2.2f), new Color(0.045f, 0.046f, 0.05f) * K("s.walls", 2.2f), new Color(0.02f, 0.02f, 0.022f) * K("s.walls", 2.2f) });
            Mesh walls = Workshop.Turned(built, line, 128, "showroom walls");
            backdrop.Add(Workshop.Piece("showroom walls", set, walls, Workshop.Flat(built, ramp, Color.white, "showroom walls"), SetLayer).GetComponent<Renderer>());

            // the polished floor, laid over its own reflection
            floorBase = new Color(0.028f, 0.03f, 0.034f) * K("s.floor", 1f);
            floor = Workshop.Floor(built, flat * 1.004f, 26, 96, "showroom floor");
            floorAt = floor.vertices;
            floorColour = floor.colors32;
            backdrop.Add(Workshop.Piece("showroom floor", set, floor, Workshop.Blended(built, Workshop.Solid(built, Color.white), "showroom floor"), SetLayer).GetComponent<Renderer>());

            // the softbox overhead, facing down
            Texture2D diffuser = Workshop.Softbox(built, 128, 96, 0.035f);
            // (brighter than white where the picture can hold it, so that the game's glow, where it is on, takes them as lights)
            float glow = K("s.glow", 1.6f);
            Material lit = Workshop.Flat(built, diffuser, new Color(glow, glow, glow, 1f), "softbox");
            var overhead = Workshop.Piece("overhead softbox", set, Workshop.Card(built, 0.62f * room, 0.42f * room, "overhead softbox"), lit, SetLayer);
            overhead.transform.localPosition = new Vector3(0f, roof - 0.01f * room, 0f);
            overhead.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            backdrop.Add(overhead.GetComponent<Renderer>());

            // two tall strip lights behind the craft, on stands
            Texture2D strip = Workshop.Softbox(built, 16, 128, 0.12f);
            Material stripLit = Workshop.Flat(built, strip, new Color(glow, glow, glow, 1f), "strip light");
            Material stand = Workshop.Flat(built, Workshop.Solid(built, Color.white), new Color(0.025f, 0.025f, 0.028f), "stand");
            float stripWide = 0.035f * room + 0.1f, low = 0.12f * roof, high = 0.86f * roof;
            foreach (float side in new[] { -1f, 1f })
            {
                // (behind the craft as the camera sees it, to either side: inside the picture at the usual lenses, near its edges)
                float a = side * 34f * Mathf.Deg2Rad;
                var at = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (0.72f * room);
                var bar = Workshop.Piece("strip light", set, Workshop.Card(built, stripWide, high - low, "strip light"), stripLit, SetLayer);
                bar.transform.localPosition = at + Vector3.up * (0.5f * (low + high));
                bar.transform.localRotation = Quaternion.LookRotation(at.normalized, Vector3.up);
                backdrop.Add(bar.GetComponent<Renderer>());
                var pole = Workshop.Piece("stand", set, Workshop.Box(built, new Vector3(0.012f * room, low, 0.012f * room), "stand"), stand, SetLayer);
                pole.transform.localPosition = at + Vector3.up * (0.5f * low);
                backdrop.Add(pole.GetComponent<Renderer>());
                var feet = Workshop.Piece("stand foot", set, Workshop.Box(built, new Vector3(0.09f * room, 0.006f * room, 0.09f * room), "stand foot"), stand, SetLayer);
                feet.transform.localPosition = at + Vector3.up * (0.003f * room);
                backdrop.Add(feet.GetComponent<Renderer>());
            }

            // the ring of light round the craft, flush with the floor
            float inner = Mathf.Max(0.62f * across + 0.4f, 1.2f), outer = inner + Mathf.Max(0.018f * inner, 0.035f);
            var ring = Workshop.Piece("turntable ring", held, Workshop.Ring(built, inner, outer, 160, "turntable ring"), Workshop.Flat(built, Workshop.Solid(built, Color.white), new Color(0.78f, 0.8f, 0.84f) * K("s.ring", 0.55f), "turntable ring"), SetLayer);
            ring.transform.localPosition = Vector3.up * 0.006f;
            backdrop.Add(ring.GetComponent<Renderer>());

            Back.backgroundColor = floorBase;
            Back.cullingMask = (1 << Parts) | (1 << SetLayer);
            mirror.On = true;
            mirror.Floor = foot;
            mirror.Up = Up;

            var left = new Vector3(-0.72f, 0.38f, 0.58f);
            var right = new Vector3(0.72f, 0.38f, 0.58f);
            Lamp("overhead", new Vector3(0.05f, 1f, -0.12f), K("s.top", 0.85f), true);
            Lamp("key", new Vector3(-0.6f, 0.5f, -0.62f), K("s.key", 0.75f), false);
            Lamp("fill", new Vector3(0.8f, 0.25f, -0.55f), K("s.fill", 0.25f), false);
            Lamp("rim left", left, K("s.rim", 0.62f), false);
            Lamp("rim right", right, K("s.rim", 0.62f), false);
            // (a dark room: a little light from everywhere, the overhead softbox broadly from above, and the strip lights from behind)
            Surround(K("s.all", 0.12f), K("s.above", 0.24f), 0f, Lobe(left, K("s.lobe", 0.1f)), Lobe(right, K("s.lobe", 0.1f)));
        }

        /// <summary>The soft shadow under the craft (worked out over the next frames; see Shade).</summary>
        void Shadow(float strength)
        {
            if (strength <= 0.001f) return;
            // (launch clamps are hidden in the studio: they cast no shadow either; nor, in flight, do other craft near by)
            var clamps = new HashSet<Collider>();
            List<Part> parts = CraftParts;
            if (parts != null)
                foreach (Part p in parts)
                    if (p != null && p.Modules.GetModule<LaunchClamp>() != null)
                        foreach (Collider c in p.GetComponentsInChildren<Collider>(true)) clamps.Add(c);
            if (InFlight)
                foreach (Vessel other in FlightGlobals.VesselsLoaded)
                    if (other != null && other != vessel)
                        foreach (Part p in other.parts)
                            if (p != null) foreach (Collider c in p.GetComponentsInChildren<Collider>(true)) clamps.Add(c);
            shade = new Shade(craft, origin, frame, built, clamps);
            shadowPaint = Workshop.Blended(built, shade.Picture, "shadow");
            Mesh card = Workshop.Card(built, shade.Size.x, shade.Size.y, "shadow");
            card.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            var thing = Workshop.Piece("shadow", held, card, shadowPaint, SetLayer);
            // (lying on the floor, its picture's x along the studio's x and its y along the studio's z)
            thing.transform.position = origin + frame * new Vector3(shade.Low.x + 0.5f * shade.Size.x, craft.min.y + 0.004f, shade.Low.y + 0.5f * shade.Size.y);
            thing.transform.rotation = frame * Quaternion.Euler(90f, 0f, 0f);
            shadowShown = thing.GetComponent<Renderer>();
            shadeFade = 0f;
            SetShadowStrength(0f);
        }

        void SetShadowStrength(float k)
        {
            if (shadowPaint == null) return;
            float s = Mathf.Clamp01(Studio.ShadowStrength) * Mathf.Clamp01(k);
            // (the particle shader takes twice its tint: 0.5 is the picture as it is)
            if (shadowPaint.HasProperty(Workshop.Tint)) shadowPaint.SetColor(Workshop.Tint, new Color(0.5f, 0.5f, 0.5f, 0.5f * s));
            else if (shadowPaint.HasProperty(Workshop.Colour)) shadowPaint.SetColor(Workshop.Colour, new Color(1f, 1f, 1f, s));
        }

        /// <summary>
        /// What the parts see reflected in themselves: the set, from where the craft stands (not the building, which the
        /// editor gives them). For the green screen, the set as a neutral grey: green must not be reflected onto the craft.
        /// </summary>
        void Reflections()
        {
            seenFrom = headingNow;
            seenAgain = Time.unscaledTime + 0.25f;
            try
            {
                if (seen == null) seen = made.Keep(new Cubemap(128, TextureFormat.ARGB32, true) { name = "Photo Studio reflections" });
                var holder = new GameObject("Photo Studio reflections eye");
                holder.transform.SetParent(root.transform, false);
                var eye = holder.AddComponent<Camera>();
                eye.enabled = false;
                eye.cullingMask = 1 << SetLayer;
                eye.clearFlags = CameraClearFlags.SolidColor;
                Color clear = Back.backgroundColor;
                Color screen = screenPaint != null ? screenPaint.GetColor(Workshop.Colour) : Color.clear;
                if (screenPaint != null)
                {
                    clear = new Color(0.42f, 0.42f, 0.42f);
                    screenPaint.SetColor(Workshop.Colour, clear);
                }
                eye.backgroundColor = clear;
                eye.nearClipPlane = 0.05f;
                eye.farClipPlane = room * 8f;
                eye.allowHDR = false; eye.allowMSAA = false;
                holder.transform.position = foot + Up * (0.5f * tall);
                bool shadowWas = shadowShown != null && shadowShown.enabled;
                if (shadowShown != null) shadowShown.enabled = false;
                var hidden = new List<Renderer>();
                if (SetLayer == Parts && CraftParts != null)
                    foreach (Part p in CraftParts)
                        if (p != null) foreach (Renderer r in p.GetComponentsInChildren<Renderer>())
                            if (!r.forceRenderingOff) { r.forceRenderingOff = true; hidden.Add(r); }
                RenderTexture active = RenderTexture.active;
                try { eye.RenderToCubemap(seen, 63); seen.Apply(true, false); }
                finally
                {
                    RenderTexture.active = active;
                    foreach (Renderer r in hidden) if (r != null) r.forceRenderingOff = false;
                    if (shadowShown != null) shadowShown.enabled = shadowWas;
                    if (screenPaint != null) screenPaint.SetColor(Workshop.Colour, screen);
                    UnityEngine.Object.Destroy(holder);
                }
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflection = seen;
                RenderSettings.reflectionIntensity = 1f;
            }
            catch (Exception ex) { Debug.Log("[Photo Studio] no reflections of the set: " + ex.Message); }
        }

        // ------------------------------------------------------------------------------------------------ the camera

        static float AngleFor(float focal) => 2f * Mathf.Atan(12f / Mathf.Max(focal, 1f)) * Mathf.Rad2Deg;   // (from top to bottom, on film 24 mm high)

        /// <summary>The whole craft in the picture, from the side it is seen from now: its middle in the middle, a little above it, filling the frame.</summary>
        public void Frame(bool keepHeading)
        {
            float up = AngleFor(Studio.Focal) * Mathf.Deg2Rad, aspect = Mathf.Max(0.2f, Eye.aspect);
            float side = 2f * Mathf.Atan(Mathf.Tan(0.5f * up) * aspect);
            float h = heading * Mathf.Deg2Rad;
            float halfWide = craft.extents.x * Mathf.Abs(Mathf.Cos(h)) + craft.extents.z * Mathf.Abs(Mathf.Sin(h));
            float halfDeep = craft.extents.x * Mathf.Abs(Mathf.Sin(h)) + craft.extents.z * Mathf.Abs(Mathf.Cos(h));
            pitch = tall > 1.5f * across ? 5f : 13f;
            height = 0.5f * tall;
            float halfHigh = 0.5f * tall * Mathf.Cos(pitch * Mathf.Deg2Rad) + halfDeep * Mathf.Sin(pitch * Mathf.Deg2Rad);
            distance = 1.12f * Mathf.Max(halfHigh / Mathf.Tan(0.5f * up), halfWide / Mathf.Tan(0.5f * side)) + halfDeep;
            distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
        }

        float MinDistance => Mathf.Max(0.6f, 0.35f * ball);
        float MaxDistance => 40f * ball + 20f;

        /// <summary>The player's hand on the camera: the right button held turns it round the craft and up and down, the wheel brings it nearer or further, the middle button held raises or lowers what it looks at; the game's camera keys too.</summary>
        public void Steer()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool free = Free(ControlTypes.CAMERACONTROLS) && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
            if (free)
            {
                if (Input.GetMouseButton(1)) { heading += Input.GetAxis("Mouse X") * 3f; pitch -= Input.GetAxis("Mouse Y") * 3f; }
                if (Input.GetMouseButton(2)) height += Input.GetAxis("Mouse Y") * distance * 0.012f;
                float wheel = GameSettings.AXIS_MOUSEWHEEL.GetAxis();
                if (wheel != 0f) distance *= Mathf.Exp(-wheel * 1.2f);
            }
            // (the game's camera keys read past the locks: in flight the game's pause, which is the studio's doing, holds them off too)
            if (!Studio.Typing())
            {
                if (GameSettings.CAMERA_ORBIT_LEFT.GetKey(true)) heading += 50f * dt;
                if (GameSettings.CAMERA_ORBIT_RIGHT.GetKey(true)) heading -= 50f * dt;
                if (GameSettings.CAMERA_ORBIT_UP.GetKey(true)) pitch += 35f * dt;
                if (GameSettings.CAMERA_ORBIT_DOWN.GetKey(true)) pitch -= 35f * dt;
                if (GameSettings.ZOOM_IN.GetKey(true)) distance *= Mathf.Exp(-1.2f * dt);
                if (GameSettings.ZOOM_OUT.GetKey(true)) distance *= Mathf.Exp(1.2f * dt);
            }
            if (Studio.Turning) heading += Studio.Spin * dt;
            pitch = Mathf.Clamp(pitch, -8f, 85f);
            distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
            height = Mathf.Clamp(height, 0f, tall);
        }

        /// <summary>
        /// Whether something is free of every lock but the studio's own and the game's pause (which in flight locks the camera
        /// too, and is the studio's doing): a settings window under the pointer, say, still holds the mouse off.
        /// </summary>
        static bool Free(ControlTypes what) => (Others() & (ulong)what) == 0;

        /// <summary>Whether every key of the keyboard is held by others, as it is while a box is being typed in.</summary>
        internal static bool KeyboardHeld => (Others() & (ulong)ControlTypes.KEYBOARDINPUT) == (ulong)ControlTypes.KEYBOARDINPUT;

        /// <summary>What the input locks hold, but the studio's own and the game's pause (in flight the studio pauses the game, and that holds everything).</summary>
        static ulong Others()
        {
            ulong others = 0;
            foreach (KeyValuePair<string, ulong> held in InputLockManager.lockStack)
                if (held.Key != Lock && held.Key != "gamePause") others |= held.Value;
            return others;
        }

        /// <summary>Set the camera by hand: heading and pitch in degrees, distance and height in metres (NaN: as it is).</summary>
        public void Aim(float toHeading, float toPitch, float toDistance, float toHeight, bool now)
        {
            if (!float.IsNaN(toHeading)) heading = toHeading;
            if (!float.IsNaN(toPitch)) pitch = Mathf.Clamp(toPitch, -8f, 85f);
            if (!float.IsNaN(toDistance)) distance = Mathf.Clamp(toDistance, MinDistance, MaxDistance);
            if (!float.IsNaN(toHeight)) height = Mathf.Clamp(toHeight, 0f, tall);
            if (now) { headingNow = heading; pitchNow = pitch; distanceNow = distance; heightNow = height; scaleNow = ScaleFor(distanceNow, pitchNow, heightNow); Place(0f); }
        }

        public string Where => "heading " + heading.ToString("F1") + " pitch " + pitch.ToString("F1") + " distance " + distance.ToString("F2") + " height " + height.ToString("F2") + " (craft " + craft.size.ToString("F2") + ", set scale " + scaleNow.ToString("F2") + ", layer " + SetLayer + ")";

        /// <summary>How much larger than its own size the set must be to keep the camera inside it (and under its ceiling).</summary>
        float ScaleFor(float far, float up, float above)
        {
            float level = far * Mathf.Cos(up * Mathf.Deg2Rad), rise = above + far * Mathf.Sin(up * Mathf.Deg2Rad);
            float s = Mathf.Max(1f, level / (0.62f * room));
            if (Kind == 2 && roof > 0f) s = Mathf.Max(s, rise / (0.85f * roof));
            return s;
        }

        /// <summary>Once a frame, after the player's hand: the camera, the set turned with it, the floor's gloss, the shadow coming in.</summary>
        public void Place(float dt)
        {
            if (closed) return;
            if (dirtyAt >= 0f && Time.unscaledTime > dirtyAt) { dirtyAt = -1f; Refit(); }
            if (InFlight) Follow();
            if (Time.unscaledTime > sweepAt) Sweep();
            NoGlow();
            float k = dt <= 0f ? 1f : 1f - Mathf.Exp(-9f * dt);
            headingNow += (heading - headingNow) * k;
            pitchNow += (pitch - pitchNow) * k;
            distanceNow = Mathf.Exp(Mathf.Lerp(Mathf.Log(distanceNow), Mathf.Log(distance), k));
            heightNow += (height - heightNow) * k;
            float wantedScale = ScaleFor(distanceNow, pitchNow, heightNow);
            scaleNow = dt <= 0f ? wantedScale : Mathf.Max(wantedScale, scaleNow + (wantedScale - scaleNow) * (1f - Mathf.Exp(-2f * dt)));

            Vector3 up = Up;
            Vector3 middle = foot + up * heightNow;
            Quaternion turn = frame * Quaternion.Euler(pitchNow, headingNow, 0f);
            Vector3 at = middle - turn * Vector3.forward * distanceNow;
            // (never under the floor, nor right on it)
            float above = Vector3.Dot(at - foot, up), lowest = 0.12f + 0.004f * distanceNow;
            if (above < lowest) at += up * (lowest - above);
            Pose(at, Quaternion.LookRotation(middle - at, up));
            Eye.fieldOfView = Mathf.Clamp(AngleFor(Studio.Focal), 1f, 120f);
            float farthest = scaleNow * room * 3.5f + 2f * roof * scaleNow + 50f;
            if (Eye.farClipPlane < farthest) Eye.farClipPlane = farthest;
            Back.nearClipPlane = Eye.nearClipPlane;
            Back.farClipPlane = Eye.farClipPlane;

            set.SetPositionAndRotation(foot, frame * Quaternion.Euler(0f, headingNow, 0f));
            set.localScale = Vector3.one * scaleNow;

            // the shadows of the craft on itself reach as far as the far side of the craft, and no further: the shadow map's
            // detail is spread over that distance, so the nearer it ends the sharper the shadows (put back on closing)
            float reach = Mathf.Clamp(distanceNow + 2.5f * ball, 15f, 4000f);
            if (Mathf.Abs(QualitySettings.shadowDistance - reach) > 0.05f * reach) QualitySettings.shadowDistance = reach;

            Probe();
            if (floor != null) Gloss(at);
            // (the showroom's lights turn with the camera: what the parts reflect is taken again when they have turned a little way)
            if (Kind == 2 && Mathf.Abs(Mathf.DeltaAngle(headingNow, seenFrom)) > 6f && Time.unscaledTime > seenAgain) Reflections();
            if (shade != null)
            {
                if (!shade.Done) shade.Work(3f);
                else if (shadeFade < 1f) { shadeFade = Mathf.Min(1f, shadeFade + (dt <= 0f ? 1f : dt * 2.5f)); SetShadowStrength(shadeFade); }
            }
            if (Kind == 1 || (Kind == 0 && !ProcessWhite)) Unprocessed();
            Focus();
        }

        // ---- depth of field, by Keystone's lens (none at f/16, where everything is sharp)
        Keystone.ILens lens;

        /// <summary>The lens set for the camera as it is now (and for whatever size it is drawing at: a picture larger than the screen too).</summary>
        public void Focus()
        {
            Keystone.ILens best = Studio.Stop < 15.9f ? Keystone.Lens.Best : null;
            if (best != lens)
            {
                if (lens != null) lens.Off();
                lens = best;
            }
            if (lens == null) return;
            lens.Use(Eye, new Keystone.Look { focal = Studio.Focal, sensorHigh = 24f, stop = Studio.Stop, focus = Mathf.Max(0.1f, Vector3.Distance(Eye.transform.position, foot + Up * heightNow)), blur = 1 });
        }

#if DEV
        public string ShadeReport => shade != null ? shade.Report() : "no shadow in this studio";
#endif

        /// <summary>The camera put where it is wanted: in the editor the camera itself; in flight the flight camera's rig, with the camera on it.</summary>
        void Pose(Vector3 at, Quaternion turned)
        {
            if (rig == null || rig == Eye.transform) { Eye.transform.SetPositionAndRotation(at, turned); return; }
            Transform eye = Eye.transform;
            Quaternion within = Quaternion.Inverse(rig.rotation) * eye.rotation;
            Vector3 offset = rig.InverseTransformPoint(eye.position);
            Quaternion rigTurned = turned * Quaternion.Inverse(within);
            rig.rotation = rigTurned;
            rig.position = at - rig.TransformVector(offset);
        }

        /// <summary>In flight: wherever the craft goes (the world's origin is moved now and then under it), the studio goes with it.</summary>
        void Follow()
        {
            if (vessel == null) return;
            Vector3 now = vessel.transform.position, moved = now - vesselAt;
            if (moved.sqrMagnitude < 1e-10f) return;
            vesselAt = now;
            origin += moved;
            foot += moved;
            if (held != null) held.position = foot;
            mirror.Floor = foot;
        }

        /// <summary>
        /// How much the polished floor reflects at each of its points, seen from where the camera is: little looking
        /// straight down, nearly all at a glancing angle (Schlick's approximation of Fresnel's law). The floor's own dark
        /// colour shows where it reflects less.
        /// </summary>
        void Gloss(Vector3 camera)
        {
            float gloss = Mathf.Clamp01(Studio.Gloss);
            float facing = 0.02f + 0.22f * gloss;
            Quaternion turn = set.rotation;
            Vector3 up = Up;
            float s = scaleNow;
            byte r = (byte)(floorBase.r * 255f), g = (byte)(floorBase.g * 255f), b = (byte)(floorBase.b * 255f);
            for (int i = 0; i < floorAt.Length; i++)
            {
                Vector3 p = foot + turn * (floorAt[i] * s);
                Vector3 d = camera - p;
                float m = d.magnitude;
                float c = m > 1e-4f ? Mathf.Clamp01(Vector3.Dot(d, up) / m) : 1f;
                float o = 1f - c, o2 = o * o;
                float reflects = gloss <= 0f ? 0f : facing + (1f - facing) * o2 * o2 * o * gloss;
                floorColour[i] = new Color32(r, g, b, (byte)Mathf.Clamp(Mathf.RoundToInt((1f - reflects) * 255f), 0, 255));
            }
            floor.colors32 = floorColour;
        }

        /// <summary>The craft was changed (or another was loaded): measure it again shortly, and put the set up round it again if it has moved or grown.</summary>
        public void Dirty() => dirtyAt = Time.unscaledTime + 0.3f;

        void Refit()
        {
            Bounds before = craft;
            float ballBefore = ball;
            if (!Measure()) return;
            HideClamps();
            if (Vector3.Distance(before.center, craft.center) < 0.01f && Vector3.Distance(before.size, craft.size) < 0.01f) return;
            Build(Kind);
            distance *= ball / Mathf.Max(ballBefore, 0.1f);
            distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
            height = Mathf.Clamp(height, 0f, tall);
        }

        // ------------------------------------------------------------------------------------------------ for the pictures

        /// <summary>Everything that is still being worked out (the shadow) finished at once, and shown in full: before a picture.</summary>
        public void Settle()
        {
            if (shade == null) return;
            if (!shade.Done) shade.Work(60000f);
            shadeFade = 1f;
            SetShadowStrength(1f);
        }

        /// <summary>The set's pieces that make up the backdrop: all but the shadow under the craft.</summary>
        public IList<Renderer> Backdrop => backdrop;

        // ------------------------------------------------------------------------------------------------ taking it down

        /// <summary>Everything put back as it was, and everything of the studio's gone.</summary>
        public void Close()
        {
            if (closed) return;
            closed = true;
            if (hooked) { Camera.onPreCull -= BeforeDrawing; hooked = false; }
            if (lens != null) { try { lens.Off(); } catch (Exception ex) { Debug.LogException(ex); } lens = null; }
            // (out of sight at once, this very frame: what is destroyed goes only at the frame's end)
            if (root != null) root.SetActive(false);
            if (mirror != null) mirror.On = false;
            GL.invertCulling = false;
            try { look.Restore(); } catch (Exception ex) { Debug.LogException(ex); }
            try { scene.Restore(); } catch (Exception ex) { Debug.LogException(ex); }
            if (root != null) UnityEngine.Object.Destroy(root);
            built.Destroy();
            made.Destroy();
        }
    }

    /// <summary>
    /// The backdrop camera's own part in the showroom: it draws what is above the floor as the floor reflects it, before
    /// the editor's camera draws the room and the craft over it. Its view is the editor camera's, turned over in the
    /// floor; nothing at or under the floor is drawn (an oblique near plane lying in the floor); and since a view seen in
    /// a mirror is turned inside out, which faces are the backs is turned round while it draws.
    /// </summary>
    sealed class Mirror : MonoBehaviour
    {
        public Camera Eye;
        public Vector3 Floor, Up = Vector3.up;                  // a point of the floor, and its up
        public bool On;
        Camera own;
        bool turned;

        void Awake() { own = GetComponent<Camera>(); }

        void OnPreCull()
        {
            if (own == null) return;
            if (!On || Eye == null)
            {
                own.ResetWorldToCameraMatrix();
                own.ResetProjectionMatrix();
                if (Eye != null) transform.SetPositionAndRotation(Eye.transform.position, Eye.transform.rotation);
                return;
            }
            // (everything turned over in the floor's plane: a point goes to its mirror image, as far under the floor as it was over it)
            Vector3 n = Up.normalized;
            float d = -Vector3.Dot(n, Floor);
            Matrix4x4 over = Matrix4x4.identity;
            over.m00 = 1f - 2f * n.x * n.x; over.m01 = -2f * n.x * n.y; over.m02 = -2f * n.x * n.z; over.m03 = -2f * d * n.x;
            over.m10 = -2f * n.y * n.x; over.m11 = 1f - 2f * n.y * n.y; over.m12 = -2f * n.y * n.z; over.m13 = -2f * d * n.y;
            over.m20 = -2f * n.z * n.x; over.m21 = -2f * n.z * n.y; over.m22 = 1f - 2f * n.z * n.z; over.m23 = -2f * d * n.z;
            Vector3 at = Eye.transform.position;
            transform.position = over.MultiplyPoint(at);
            Vector3 forward = Eye.transform.forward, up = Eye.transform.up;
            forward -= 2f * Vector3.Dot(forward, n) * n; up -= 2f * Vector3.Dot(up, n) * n;
            transform.rotation = Quaternion.LookRotation(forward, up);
            own.worldToCameraMatrix = Eye.worldToCameraMatrix * over;
            // the near plane laid in the floor, a hair above it, so that nothing at or under the floor is reflected
            Matrix4x4 view = own.worldToCameraMatrix;
            Vector3 point = view.MultiplyPoint(Floor + n * 0.004f);
            Vector3 normal = view.MultiplyVector(n).normalized;
            own.projectionMatrix = Eye.CalculateObliqueMatrix(new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(point, normal)));
        }

        void OnPreRender()
        {
            if (!On) return;
            turned = true;
            GL.invertCulling = true;
        }

        void OnPostRender()
        {
            if (!turned) return;
            turned = false;
            GL.invertCulling = false;
        }

        void OnDisable()
        {
            if (turned) { turned = false; GL.invertCulling = false; }
        }
    }
}
