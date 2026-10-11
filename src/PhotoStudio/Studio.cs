using System;
using System.Collections;
using Keystone;
using UnityEngine;
using UnityEngine.EventSystems;

#if !DEV
[assembly: KSPAssembly("PhotoStudio", 0, 1)]
[assembly: KSPAssemblyDependency("Keystone", 0, 4)]
#endif

namespace PhotoStudio
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class StudioAtStart : MonoBehaviour
    {
        void Awake() { Studio.Setup(); Destroy(gameObject); }
    }

    /// <summary>
    /// A photographer's studio for the craft being built, in the assembly building and the hangar: the building and its
    /// crew go away and the craft stands in a white cyclorama, before a green screen, or in a dark showroom on a polished
    /// floor, lit as a studio lights things; a camera goes round it; and pictures of it can be taken larger than the
    /// screen, with the background see-through if wanted. Closing the studio puts everything back as it was. In flight
    /// the same, round the craft being flown, with the game paused while the studio is up.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
    public sealed class Studio : MonoBehaviour
    {
        public const string Version = "0.1.0";
        public static readonly string[] Studios = { "white", "green screen", "showroom" };
        static readonly string[] Named = { "white studio", "green screen", "showroom" };
        static readonly string[] Sizes = { "the screen's", "twice", "three times", "four times" };

        static Mod mod;
        static Choice kind, size;
        static Slider focal, stop, spin, white, shadow, gloss;
        static Toggle turning, clear, hide;
        static Choice side;
        static Tint screen;

        public static float Focal => focal != null ? focal.Value : 50f;
        public static float Stop => stop != null ? stop.Value : 16f;
        public static bool Turning => turning != null && turning.Value;
        public static float Spin => spin != null ? spin.Value : 8f;
        public static float WhiteLevel => white != null ? white.Value : 1f;
        public static float ShadowStrength => shadow != null ? shadow.Value : 0.75f;
        public static float Gloss => gloss != null ? gloss.Value : 0.75f;
        public static Color ScreenColour => screen != null ? screen.Value : new Color(0f, 0.694f, 0.251f);
        public static bool HideInterface => hide == null || hide.Value;
        public static bool KeyOnRight => side != null && side.Value == 1;

        public static void Setup()
        {
            if (mod != null && Kit.Find("Photo Studio") == mod) return;
            mod = Kit.Register("Photo Studio", Version, "The craft you are building, in a photographer's studio: a white cyclorama, a green screen or a showroom. A camera that goes round it, a turntable, and pictures larger than the screen. In the assembly building and the hangar.");
            kind = mod.Choice("studio", "Studio", 0, Studios, "White: a seamless white floor and wall with a soft shadow under the craft, lit as products are photographed. Green screen: one even chroma green, no shadow on it, for keying. Showroom: a dark room, a polished floor that reflects the craft, a softbox overhead and strip lights behind.");
            mod.Heading("Camera");
            focal = mod.Slider("focal", "Lens", 50f, 18f, 200f, " mm", 0, "The focal length, as on a camera with film 24 mm high: a short lens sees wide and exaggerates depth, a long one flattens it. 50 to 100 mm is what product photographers use.");
            stop = mod.Slider("stop", "Aperture  f /", 16f, 1.4f, 16f, "", 1, "The lens's f-number, for depth of field: at 16 everything is sharp; open the lens wider (smaller numbers) and what is nearer or further than the craft is blurred, more the closer the camera and the longer the lens. Done by Keystone's lens (on Windows only where TUFX is installed); the craft is kept in focus.");
            side = mod.Choice("side", "Key light from the", 0, new[] { "left", "right" }, "Which side of the picture the main light comes from (and its shadows fall away from); the other lights go round with it.");
            turning = mod.Toggle("turntable", "Turntable", false, "The craft turns slowly, as on a photographer's turntable: the camera, the lights and the set stay as they are.");
            spin = mod.Slider("spin", "Turntable speed", 8f, -40f, 40f, " ° a second", 1, "How fast the turntable turns; less than nought, the other way.");
            mod.Heading("Pictures");
            size = mod.Choice("size", "Size", 1, Sizes, "How large a picture is, against the screen's size: twice is twice as wide and twice as high. (At most 8192 pixels on its longer side.)");
            clear = mod.Toggle("clear", "See-through background", false, "In the white studio and before the green screen: the picture has no background, only the craft (and in the white studio its shadow, as a see-through shade), for putting onto anything. Drawn without the game's post-processing.");
            mod.Heading("The studios");
            white = mod.Slider("white", "White backdrop", 1f, 0.6f, 1f, "", 2, "How bright the white studio's backdrop is: 1 is as white as the screen can show; less is a light grey, which parts a white craft from it more.");
            shadow = mod.Slider("shadow", "Shadow under the craft", 0.75f, 0f, 1f, "", 2, "How dark the soft shadow on the white studio's floor is.");
            screen = mod.Tint("screen", "Screen colour", new Color(0f, 0.694f, 0.251f), "The green screen's colour: chroma green to begin with (#00B140). Chroma blue is #0047BB.");
            gloss = mod.Slider("gloss", "Showroom floor gloss", 0.75f, 0f, 1f, "", 2, "How much the showroom's floor reflects: nought is matt, one is a mirror-like lacquer.");
            hide = mod.Toggle("hide", "Put the game's interface away", true, "While the studio is open the editor's own panels and buttons are put away (the studio closes with Option-P, Alt-P elsewhere, and the settings window opens with the modifier key and K as always).");
        }

        public static Studio Running { get; private set; }

        Stage stage;
        string said = "", hint = "";
        float hintUntil, rebuildAt = -1f;
        string lastPicture;
        bool shooting;

        void Awake()
        {
            Setup();
            Running = this;
            mod.Panel = Panel;
            kind.Changed += Changed;
            white.Changed += Rebuild;
            shadow.Changed += Rebuild;
            screen.Changed += Rebuild;
            side.Changed += Rebuild;
            GameEvents.onEditorRestart.Add(Restarted);
            GameEvents.onEditorShipModified.Add(Modified);
            GameEvents.onEditorLoad.Add(Loaded);
            GameEvents.onGameSceneLoadRequested.Add(Leaving);
            GameEvents.onVesselChange.Add(VesselChanged);
            GameEvents.onVesselWillDestroy.Add(VesselGoing);
            GameEvents.OnMapEntered.Add(MapEntered);
            GameEvents.OnCameraChange.Add(CameraChanged);
        }

        void OnDestroy()
        {
            Close();
            if (escaping) { escaping = false; InputLockManager.RemoveControlLock(EscapeLock); }
            if (kind != null) kind.Changed -= Changed;
            if (white != null) white.Changed -= Rebuild;
            if (shadow != null) shadow.Changed -= Rebuild;
            if (screen != null) screen.Changed -= Rebuild;
            if (side != null) side.Changed -= Rebuild;
            GameEvents.onEditorRestart.Remove(Restarted);
            GameEvents.onEditorShipModified.Remove(Modified);
            GameEvents.onEditorLoad.Remove(Loaded);
            GameEvents.onGameSceneLoadRequested.Remove(Leaving);
            GameEvents.onVesselChange.Remove(VesselChanged);
            GameEvents.onVesselWillDestroy.Remove(VesselGoing);
            GameEvents.OnMapEntered.Remove(MapEntered);
            GameEvents.OnCameraChange.Remove(CameraChanged);
            if (mod != null && mod.Panel == Panel) mod.Panel = null;
            if (Running == this) Running = null;
        }

        // (The editor starts afresh when a craft is loaded, a new one begun, or the other building gone to: the studio is put away
        // first, everything back, and put up again round whatever craft there is once the editor has settled.)
        void Restarted()
        {
            if (stage == null) return;
            Close();
            reopenAt = Time.unscaledTime + 0.8f;
        }

        float reopenAt = -1f;
        void Modified(ShipConstruct ship) { stage?.Dirty(); }
        void Loaded(ShipConstruct ship, KSP.UI.Screens.CraftBrowserDialog.LoadType how) { stage?.Dirty(); }
        void Leaving(GameScenes to) { reopenAt = -1f; Close(); }
        // (in flight: another craft taken over, the craft gone, the map, the view from inside: the studio is put away)
        void VesselChanged(Vessel to) { if (stage != null && stage.InFlight) { Close(); Tell("The studio is closed: another craft is being flown."); } }
        void VesselGoing(Vessel going) { if (stage != null && stage.InFlight && going == FlightGlobals.ActiveVessel) Close(); }
        void MapEntered() { if (stage != null && stage.InFlight) Close(); }
        void CameraChanged(CameraManager.CameraMode to) { if (stage != null && stage.InFlight && to != CameraManager.CameraMode.Flight) Close(); }
        void Changed() { if (stage != null) { stage.Build(kind.Value); Tell(Named[kind.Value]); } }
        void Rebuild() { if (stage != null) rebuildAt = Time.unscaledTime + 0.25f; }

        public string Open()
        {
            if (stage != null) return "open";
            stage = Stage.Open(kind.Value, out string why);
            if (stage == null) { Tell(why); return why; }
            Tell(Named[kind.Value] + ": right button to turn, wheel to come nearer, middle button to raise.  " + Key + "-1, 2, 3: the studios; " + Key + "-T: turntable; " + Key + "-" + Shutter + ": a picture; " + Key + "-P: close.", 8f);
            return "open";
        }

        public void Close()
        {
            if (stage == null) return;
            Stage was = stage;
            stage = null;
            was.Close();
        }

        static string Key => Application.platform == RuntimePlatform.OSXPlayer ? "Option" : "Alt";

        /// <summary>The key that takes a picture, with Key held: Return, or S. (On Windows Alt-Return never reaches a mod: the game's window
        /// keeps it for itself. So S is the one named there; both work everywhere.)</summary>
        static string Shutter => Application.platform == RuntimePlatform.WindowsPlayer ? "S" : "Return";

        void Tell(string text, float seconds = 4f)
        {
            said = text ?? "";
            hint = said;
            hintUntil = Time.unscaledTime + seconds;
        }

        /// <summary>Whether something is being typed into (a box of the game's or of a settings window): then the keys are not the studio's.</summary>
        public static bool Typing()
        {
            // (a box being typed in locks every key of the keyboard; the studio's own lock, and others, lock only some: "any of them locked" would be always.
            // While the studio is open the game's pause is passed over as well: in flight the studio pauses the game, and that locks every key)
            bool open = Running != null && Running.stage != null;
            if (GUIUtility.keyboardControl != 0 || (open ? Stage.KeyboardHeld : InputLockManager.IsAllLocked(ControlTypes.KEYBOARDINPUT))) return true;
            GameObject chosen = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return chosen != null && (chosen.GetComponent("TMP_InputField") != null || chosen.GetComponent<UnityEngine.UI.InputField>() != null);
        }

        const string EscapeLock = "PhotoStudio_Escape";
        bool escaping;                                  // Escape closed the studio: the game's pause menu is held off until the key is let go

        void Update()
        {
            if (escaping && !GameSettings.PAUSE.GetKey(true) && !GameSettings.PAUSE.GetKeyUp(true)) { escaping = false; InputLockManager.RemoveControlLock(EscapeLock); }
            if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight) return;
            if (reopenAt >= 0f && Time.unscaledTime > reopenAt && HighLogic.LoadedSceneIsEditor)
            {
                reopenAt = -1f;
                if (stage == null && EditorLogic.fetch != null && EditorLogic.fetch.ship != null && EditorLogic.fetch.ship.parts.Count > 0) Open();
                else if (stage == null) Tell("The studio closed: the editor started afresh.");
            }
            // (Escape leaves the studio, as it leaves a game's photo mode, and does not open the game's menu as well: while the
            // studio is open the menu is held off, since with the interface put away it would open where it cannot be seen)
            if (stage != null && GameSettings.PAUSE.GetKeyDown(true) && !Typing())
            {
                escaping = true;
                InputLockManager.SetControlLock(ControlTypes.PAUSE, EscapeLock);
                Close();
                Tell("The studio is closed.");
                return;
            }
            // (read past the locks, as Escape is: in flight the studio's own lock, which holds off everything but the camera, holds the
            // game's modifier key off too, and none of the studio's keys would work there)
            bool held = GameSettings.MODIFIER_KEY.GetKey(true);
            if (held && Input.GetKeyDown(KeyCode.P) && !Typing())
            {
                if (stage == null) Open();
                else { Close(); Tell("The studio is closed."); }
            }
            if (stage == null) return;
            // (in flight, Cinema Camera switched on while the studio is open would move the same camera: the studio gives way)
            if (stage.InFlight && Time.frameCount % 15 == 0 && Stage.CinemaCameraOn) { Close(); Tell("The studio closed: Cinema Camera was switched on."); return; }
            if (held && !Typing())
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.S)) Shoot();
                // (the studios by number, and the turntable, without opening the settings window)
                if (Input.GetKeyDown(KeyCode.Alpha1)) kind.Value = 0;
                if (Input.GetKeyDown(KeyCode.Alpha2)) kind.Value = 1;
                if (Input.GetKeyDown(KeyCode.Alpha3)) kind.Value = 2;
                if (Input.GetKeyDown(KeyCode.T)) { turning.Value = !turning.Value; Tell(turning.Value ? "The turntable turns." : "The turntable stops."); }
            }
            try { stage.Steer(); }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Close();
                Tell("The studio closed after a fault (" + ex.Message + "); everything is as it was.");
            }
        }

        void LateUpdate()
        {
            if (stage == null) return;
            if (rebuildAt >= 0f && Time.unscaledTime > rebuildAt) { rebuildAt = -1f; stage.Build(kind.Value); }
            try { stage.Place(Time.unscaledDeltaTime); }
            catch (Exception ex)
            {
                // (whatever went wrong, the editor must not be left without its building and its camera)
                Debug.LogException(ex);
                Close();
                Tell("The studio closed after a fault (" + ex.Message + "); everything is as it was.");
            }
        }

        /// <summary>A picture, taken once this frame has been drawn.</summary>
        public void Shoot()
        {
            if (stage == null || shooting) return;
            StartCoroutine(Shooting());
        }

        IEnumerator Shooting()
        {
            shooting = true;
            Tell("Taking the picture...", 30f);
            yield return new WaitForEndOfFrame();
            try { Took(TakeNow()); }
            finally { shooting = false; }
        }

        string TakeNow()
        {
            if (stage == null) return null;
            bool see = clear.Value && stage.Kind != 2;
            string ship = CraftName();
            try { stage.Settle(); return Picture.Take(stage, size.Value + 1, see, ship, Named[stage.Kind]); }
            catch (Exception ex) { Debug.LogException(ex); Tell("The picture could not be taken: " + ex.Message, 8f); return null; }
        }

        /// <summary>The craft's name: the editor's craft, or the craft being flown.</summary>
        static string CraftName()
        {
            if (HighLogic.LoadedSceneIsFlight) return FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.GetDisplayName() : "";
            return EditorLogic.fetch != null && EditorLogic.fetch.ship != null ? Shown(EditorLogic.fetch.ship.shipName) : "";
        }

        static string Shown(string name) => string.IsNullOrEmpty(name) ? name : KSP.Localization.Localizer.Format(name);

        void Took(string path)
        {
            if (path == null) return;
            lastPicture = path;
            Tell("Saved: " + System.IO.Path.GetFileName(path) + "  (in the game's Screenshots folder)", 6f);
            Debug.Log("[Photo Studio] picture saved: " + path);
        }

        void OnGUI()
        {
            if (stage == null && Time.unscaledTime > hintUntil) return;
            if (Event.current.type != EventType.Repaint || Time.unscaledTime > hintUntil || string.IsNullOrEmpty(hint)) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, wordWrap = false };
            float fade = Mathf.Clamp01(hintUntil - Time.unscaledTime);
            var where = new Rect(0f, Screen.height - 44f, Screen.width, 24f);
            Color was = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f * fade);
            GUI.Label(new Rect(where.x + 1f, where.y + 1f, where.width, where.height), hint, style);
            GUI.color = new Color(1f, 1f, 1f, 0.92f * fade);
            GUI.Label(where, hint, style);
            GUI.color = was;
        }

        static GUIStyle style;

        void Panel()
        {
            GUILayout.Space(4f);
            if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight) { GUILayout.Label("The studio opens in the assembly building, the hangar, and in flight.", Host.Small); return; }
            GUILayout.BeginHorizontal();
            if (stage == null)
            {
                if (GUILayout.Button("Open the studio  (" + Key + "-P)")) Open();
            }
            else
            {
                if (GUILayout.Button("Close the studio")) { Close(); Tell("The studio is closed."); }
                if (GUILayout.Button("Frame the craft")) stage.Frame(true);
                if (GUILayout.Button(shooting ? "Taking..." : "Take a picture")) Shoot();
            }
            GUILayout.EndHorizontal();
            string about = stage == null ? "" : "Right button: turn round the craft and up and down. Wheel: nearer and further. Middle button: raise and lower the view. " + Key + "-1, 2 and 3: the white studio, the green screen, the showroom. " + Key + "-T: the turntable. " + Key + "-" + Shutter + ": a picture. " + Key + "-P or Escape: close.";
            if (stage != null && stage.Shading) about += "\nWorking out the shadow under the craft...";
            if (stage != null && clear.Value && stage.Kind == 2) about += "\n(The showroom's pictures always have their background: its floor's reflection is part of the picture.)";
            if (!string.IsNullOrEmpty(lastPicture)) about += "\nLast picture: " + System.IO.Path.GetFileName(lastPicture);
            if (!string.IsNullOrEmpty(said) && stage == null) about += (about.Length > 0 ? "\n" : "") + said;
            if (about.Length > 0) GUILayout.Label(about, Host.Small);
        }

#if DEV
        /// <summary>(For the development build: open the studio, as one of the studios by name or number.)</summary>
        public static string Open(string which)
        {
            if (Running == null) return "not in the editor or in flight";
            int n = Array.IndexOf(Studios, which);
            if (n < 0 && int.TryParse(which, out int k)) n = k;
            if (n >= 0) kind.Value = n;
            string said = Running.Open();
            return said + "; " + (Running.stage != null ? Running.stage.Where : "");
        }

        public static string Shut()
        {
            if (Running == null) return "not in the editor";
            Running.Close();
            return "closed";
        }

        /// <summary>(For the development build: the camera set by hand, "heading pitch distance height", a dash for any to leave.)</summary>
        public static string Aim(string what)
        {
            if (Running == null || Running.stage == null) return "not open";
            string[] parts = (what ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float Get(int i) => i < parts.Length && parts[i] != "-" && float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : float.NaN;
            if (parts.Length > 0 && parts[0] == "frame") Running.stage.Frame(true);
            else Running.stage.Aim(Get(0), Get(1), Get(2), Get(3), true);
            return Running.stage.Where;
        }

        /// <summary>(For the development build: take a picture at once; "2 clear" for twice the screen's size and see-through.)</summary>
        public static string Shot(string what)
        {
            if (Running == null || Running.stage == null) return "not open";
            string[] parts = (what ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int times = parts.Length > 0 && int.TryParse(parts[0], out int t) ? t : size.Value + 1;
            bool see = Array.IndexOf(parts, "clear") >= 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Running.stage.Settle();
            string path = Picture.Take(Running.stage, times, see && Running.stage.Kind != 2, CraftName(), Named[Running.stage.Kind]);
            return path + " (" + watch.ElapsedMilliseconds + " ms)";
        }

        /// <summary>(For the development build: the white studio with the game's post-processing kept, or held off as it ships.)</summary>
        public static string Processing()
        {
            Stage.ProcessWhite = !Stage.ProcessWhite;
            if (Running != null && Running.stage != null) Running.stage.Build(Running.stage.Kind);
            return "post-processing in the white studio " + (Stage.ProcessWhite ? "kept" : "held off");
        }

        /// <summary>(For the development build: the studios' lighting changed while the game runs, "w.key=1.1 w.all=0.4"; "reset" for all as made. The studio is put up again.)</summary>
        public static string Tune(string what)
        {
            foreach (string pair in (what ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (pair == "reset") { Stage.Knobs.Clear(); continue; }
                string[] kv = pair.Split('=');
                if (kv.Length == 2 && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) Stage.Knobs[kv[0]] = v;
            }
            if (Running != null && Running.stage != null) Running.stage.Build(Running.stage.Kind);
            var said = new System.Text.StringBuilder();
            foreach (var kv in Stage.Knobs) said.Append(kv.Key).Append('=').Append(kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            return said.Length > 0 ? said.ToString() : "all as made";
        }

        /// <summary>(For the development build: the shadow's working.)</summary>
        public static string ShadeReport() => Running == null || Running.stage == null ? "closed" : Running.stage.ShadeReport;

        public static string Where() => Running == null ? "not in the editor" : Running.stage == null ? "closed" : Running.stage.Where + (Running.stage.Shading ? ", shadow being worked out" : "");

        /// <summary>(For the development build: what the editor's launch button does.)</summary>
        public static string Launch()
        {
            if (EditorLogic.fetch == null) return "not in the editor";
            EditorLogic.fetch.launchVessel();
            return "launching";
        }
#endif
    }
}
