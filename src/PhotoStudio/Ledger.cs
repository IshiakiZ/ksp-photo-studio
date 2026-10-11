using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhotoStudio
{
    /// <summary>
    /// Everything the studio changes in the game's own scene, written down as it is changed, with what it was before,
    /// so that all of it can be put back exactly, last change first. Nothing is ever changed without being written
    /// down here first. Whatever has been destroyed in the meantime (the game took the scene down) is passed over.
    /// </summary>
    sealed class Ledger
    {
        readonly List<Action> undo = new List<Action>();

        public int Count => undo.Count;

        /// <summary>Something to do to put one change back.</summary>
        public void Add(Action putBack) => undo.Add(putBack);

        /// <summary>Everything back as it was, the last change first.</summary>
        public void Restore()
        {
            for (int i = undo.Count - 1; i >= 0; i--)
            {
                try { undo[i](); }
                catch (Exception ex) { Debug.Log("[Photo Studio] could not put something back: " + ex.Message); }
            }
            undo.Clear();
        }

        /// <summary>A component switched on or off.</summary>
        public void Enable(Behaviour thing, bool value)
        {
            if (thing == null || thing.enabled == value) return;
            bool was = thing.enabled;
            Add(() => { if (thing != null) thing.enabled = was; });
            thing.enabled = value;
        }

        /// <summary>Something hidden from every camera, without being switched off (its owner goes on as before).</summary>
        public void Hide(Renderer thing)
        {
            if (thing == null || thing.forceRenderingOff) return;
            Add(() => { if (thing != null) thing.forceRenderingOff = false; });
            thing.forceRenderingOff = true;
        }

        /// <summary>A part that takes no glow of the game's under the pointer (the kind of glow it took is put back), and any it has now put out.</summary>
        public void NoGlow(Part part)
        {
            if (part == null) return;
            if (part.highlightType != Part.HighlightType.Disabled)
            {
                Part.HighlightType was = part.highlightType;
                Add(() => { if (part != null && part.highlightType == Part.HighlightType.Disabled) part.SetHighlightType(was); });
                part.highlightType = Part.HighlightType.Disabled;
            }
            if (part.HighlightActive) part.SetHighlight(false, false);
        }

        /// <summary>What a camera draws.</summary>
        public void Mask(Camera camera, int mask)
        {
            if (camera == null || camera.cullingMask == mask) return;
            int was = camera.cullingMask;
            Add(() => { if (camera != null) camera.cullingMask = was; });
            camera.cullingMask = mask;
        }

        /// <summary>A camera's angle of view and how near and how far it sees, all three kept together.</summary>
        public void Lens(Camera camera)
        {
            if (camera == null) return;
            float angle = camera.fieldOfView, near = camera.nearClipPlane, far = camera.farClipPlane;
            Add(() => { if (camera != null) { camera.fieldOfView = angle; camera.nearClipPlane = near; camera.farClipPlane = far; } });
        }

        /// <summary>Where a thing is: its parent, and its place and turn within that parent. (The parent itself is not touched: in flight it can be a part of the craft.)</summary>
        public void Place(Transform thing)
        {
            if (thing == null) return;
            Transform parent = thing.parent;
            Vector3 at = thing.localPosition; Quaternion turned = thing.localRotation;
            Add(() =>
            {
                if (thing == null) return;
                if (parent != null && thing.parent != parent) thing.SetParent(parent, false);
                thing.localPosition = at; thing.localRotation = turned;
            });
        }

        /// <summary>The scene's light from all round, its reflections and its fog (RenderSettings), all kept together.</summary>
        public void Surroundings()
        {
            AmbientMode mode = RenderSettings.ambientMode;
            Color flat = RenderSettings.ambientLight, sky = RenderSettings.ambientSkyColor, equator = RenderSettings.ambientEquatorColor, ground = RenderSettings.ambientGroundColor;
            float intensity = RenderSettings.ambientIntensity;
            SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
            Cubemap reflection = RenderSettings.customReflection;
            DefaultReflectionMode reflectionMode = RenderSettings.defaultReflectionMode;
            float reflectionIntensity = RenderSettings.reflectionIntensity;
            bool fog = RenderSettings.fog;
            Add(() =>
            {
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientLight = flat;
                RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator; RenderSettings.ambientGroundColor = ground;
                RenderSettings.ambientIntensity = intensity;
                RenderSettings.ambientProbe = probe;
                RenderSettings.customReflection = reflection;
                RenderSettings.defaultReflectionMode = reflectionMode;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.fog = fog;
            });
        }

        /// <summary>How far from the camera shadows are drawn (a setting of the whole game, not of the scene: it outlives the scene).</summary>
        public void ShadowReach(float metres)
        {
            if (Mathf.Abs(QualitySettings.shadowDistance - metres) < 0.01f) return;
            float was = QualitySettings.shadowDistance;
            Add(() => QualitySettings.shadowDistance = was);
            QualitySettings.shadowDistance = metres;
        }
    }
}
