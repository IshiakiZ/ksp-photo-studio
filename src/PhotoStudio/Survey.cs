#if DEV
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhotoStudio
{
    /// <summary>For the development build only (called through the bridge's dev_call): what the scene is made of, and a fingerprint of everything the studio may touch, to compare before and after.</summary>
    public static class Survey
    {
        static string Path(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; t != null && i < 7; i++, t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts.ToArray());
        }

        static string Layers(int mask)
        {
            var on = new List<string>();
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) on.Add(i.ToString());
            return mask == -1 ? "all" : string.Join(",", on.ToArray());
        }

        /// <summary>The cameras, lights, layers, the light from all round, and which of the shaders the studio might use the game has.</summary>
        public static string Scene()
        {
            var say = new StringBuilder();
            say.Append("LAYERS:");
            for (int i = 0; i < 32; i++) say.Append(' ').Append(i).Append('=').Append(LayerMask.LayerToName(i));
            say.Append('\n');
            foreach (Camera c in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                say.Append("CAMERA ").Append(Path(c.transform)).Append(c.enabled ? "" : " (off)").Append(" depth ").Append(c.depth).Append(" mask ").Append(Layers(c.cullingMask))
                   .Append(" clear ").Append(c.clearFlags).Append(" bg ").Append(c.backgroundColor.ToString("F2")).Append(" fov ").Append(c.fieldOfView.ToString("F1")).Append(" near ").Append(c.nearClipPlane.ToString("F2")).Append(" far ").Append(c.farClipPlane.ToString("F0"))
                   .Append(c.allowHDR ? " hdr" : "").Append(c.allowMSAA ? " msaa" : "").Append(" path ").Append(c.actualRenderingPath).Append(c.targetTexture != null ? " into " + c.targetTexture.name : "").Append(" at ").Append(c.transform.position.ToString("F1")).Append(" components:");
                foreach (Component k in c.GetComponents<Component>()) say.Append(' ').Append(k.GetType().Name).Append(k is Behaviour b && !b.enabled ? "(off)" : "");
                say.Append('\n');
            }
            foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>())
                say.Append("LIGHT ").Append(Path(l.transform)).Append(l.enabled ? "" : " (off)").Append(' ').Append(l.type).Append(" intensity ").Append(l.intensity.ToString("F2")).Append(" colour ").Append(l.color.ToString("F2"))
                   .Append(" mask ").Append(Layers(l.cullingMask)).Append(" shadows ").Append(l.shadows).Append(" mode ").Append(l.renderMode).Append(" forward ").Append(l.transform.forward.ToString("F2")).Append('\n');
            var perLayer = new int[32];
            var names = new Dictionary<int, HashSet<string>>();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || r.forceRenderingOff) continue;
                int layer = r.gameObject.layer;
                perLayer[layer]++;
                if (r.GetComponentInParent<Part>() != null) continue;
                if (!names.TryGetValue(layer, out HashSet<string> set)) names[layer] = set = new HashSet<string>();
                if (set.Count < 14) set.Add(Path(r.transform.root) + ">" + r.name + "(" + (r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "-") + ")");
            }
            for (int i = 0; i < 32; i++)
                if (perLayer[i] > 0) say.Append("RENDERERS on ").Append(i).Append(": ").Append(perLayer[i]).Append(names.ContainsKey(i) ? " e.g. " + string.Join(" | ", new List<string>(names[i]).ToArray()) : " (all parts)").Append('\n');
            say.Append("AMBIENT ").Append(RenderSettings.ambientMode).Append(" flat ").Append(RenderSettings.ambientLight.ToString("F3")).Append(" sky ").Append(RenderSettings.ambientSkyColor.ToString("F3")).Append(" equator ").Append(RenderSettings.ambientEquatorColor.ToString("F3"))
               .Append(" ground ").Append(RenderSettings.ambientGroundColor.ToString("F3")).Append(" intensity ").Append(RenderSettings.ambientIntensity.ToString("F2")).Append(" reflection ").Append(RenderSettings.defaultReflectionMode).Append(' ')
               .Append(RenderSettings.customReflection != null ? RenderSettings.customReflection.name + " " + RenderSettings.customReflection.width : "none").Append(" x").Append(RenderSettings.reflectionIntensity.ToString("F2")).Append(" fog ").Append(RenderSettings.fog)
               .Append(" skybox ").Append(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none").Append('\n');
            say.Append("QUALITY shadows ").Append(QualitySettings.shadows).Append(" distance ").Append(QualitySettings.shadowDistance.ToString("F0")).Append(" cascades ").Append(QualitySettings.shadowCascades).Append(" resolution ").Append(QualitySettings.shadowResolution)
               .Append(" pixel lights ").Append(QualitySettings.pixelLightCount).Append(" msaa ").Append(QualitySettings.antiAliasing).Append(" colour space ").Append(QualitySettings.activeColorSpace).Append(" device ").Append(SystemInfo.graphicsDeviceType).Append(" max texture ").Append(SystemInfo.maxTextureSize).Append('\n');
            string[] wanted = { "KSP/Unlit", "KSP/Diffuse", "KSP/Specular", "KSP/Bumped Specular", "KSP/Emissive/Diffuse", "KSP/Alpha/Unlit Transparent", "KSP/Alpha/Translucent", "KSP/Particles/Alpha Blended", "KSP/Particles/Additive",
                                "Legacy Shaders/Particles/Alpha Blended", "Legacy Shaders/Particles/Additive", "Legacy Shaders/Particles/Alpha Blended Premultiply", "Legacy Shaders/Diffuse", "Legacy Shaders/VertexLit", "Unlit/Color", "Unlit/Texture", "Unlit/Transparent",
                                "Unlit/Transparent Cutout", "Sprites/Default", "UI/Default", "Hidden/Internal-Colored", "Standard", "Diffuse", "Particles/Standard Unlit", "KSP/Scenery/Invisible Shadow Receiver", "KSP/Scenery/Unlit", "KSP/Scenery/Diffuse", "Skybox/Procedural" };
            say.Append("SHADERS:");
            foreach (string name in wanted)
            {
                Shader s = Shader.Find(name);
                say.Append(' ').Append(name).Append(s == null ? "=none" : s.isSupported ? "=yes" : "=unsupported");
                if (s != null && s.isSupported)
                {
                    var props = new List<string>();
                    for (int i = 0; i < s.GetPropertyCount() && i < 12; i++) props.Add(s.GetPropertyName(i));
                    say.Append('[').Append(string.Join(",", props.ToArray())).Append(']');
                }
            }
            say.Append('\n');
            if (EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
            {
                say.Append("SHIP ").Append(EditorLogic.fetch.ship.shipName).Append(", ").Append(EditorLogic.fetch.ship.parts.Count).Append(" parts");
                foreach (Part p in EditorLogic.fetch.ship.parts)
                {
                    var shaders = new HashSet<string>();
                    foreach (Renderer r in p.GetComponentsInChildren<Renderer>()) foreach (Material m in r.sharedMaterials) if (m != null && m.shader != null) shaders.Add(m.shader.name);
                    say.Append("; ").Append(p.partInfo != null ? p.partInfo.name : p.name).Append(" layer ").Append(p.gameObject.layer).Append(" [").Append(string.Join(",", new List<string>(shaders).ToArray())).Append(']');
                }
            }
            return say.ToString();
        }

        /// <summary>
        /// Everything about the scene that the studio changes or might leave behind, as one text: cameras, lights,
        /// what is hidden, the light from all round, the game's shadow settings, the input locks, and how many things
        /// there are. Taken before the studio is opened and after it is closed, the two must be the same.
        /// </summary>
        public static string Fingerprint()
        {
            var say = new StringBuilder();
            var lines = new List<string>();
            foreach (Camera c in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                var line = new StringBuilder();
                line.Append("camera ").Append(Path(c.transform)).Append(' ').Append(c.enabled).Append(' ').Append(c.depth).Append(' ').Append(c.cullingMask).Append(' ').Append(c.clearFlags).Append(' ').Append(c.backgroundColor.ToString("F3"))
                    .Append(" fov ").Append(c.fieldOfView.ToString("F3")).Append(' ').Append(c.nearClipPlane.ToString("F3")).Append(' ').Append(c.farClipPlane.ToString("F1")).Append(' ').Append(c.allowHDR).Append(' ').Append(c.depthTextureMode)
                    .Append(" at ").Append(c.transform.position.ToString("F3")).Append(' ').Append(c.transform.rotation.eulerAngles.ToString("F2")).Append(" parent ").Append(c.transform.parent != null ? c.transform.parent.position.ToString("F3") : "-");
                foreach (Behaviour b in c.GetComponents<Behaviour>()) line.Append(' ').Append(b.GetType().Name).Append('=').Append(b.enabled);
                lines.Add(line.ToString());
            }
            foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>())
                lines.Add("light " + Path(l.transform) + " " + l.enabled + " " + l.intensity.ToString("F3") + " " + l.color.ToString("F3") + " " + l.cullingMask + " " + l.shadows + " " + l.transform.rotation.eulerAngles.ToString("F2"));
            int renderers = 0, hidden = 0, off = 0;
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                renderers++;
                if (r.forceRenderingOff) { hidden++; lines.Add("hidden " + Path(r.transform)); }
                if (!r.enabled) off++;
            }
            lines.Sort(StringComparer.Ordinal);
            foreach (string line in lines) say.Append(line).Append('\n');
            say.Append("renderers ").Append(renderers).Append(", hidden ").Append(hidden).Append(", switched off ").Append(off).Append('\n');
            int objects = UnityEngine.Object.FindObjectsOfType<Transform>().Length;
            int ours = 0;
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>()) if (t.name.StartsWith("Photo Studio", StringComparison.Ordinal)) ours++;
            say.Append("objects ").Append(objects).Append(", of the studio's ").Append(ours).Append('\n');
            say.Append("meshes ").Append(Resources.FindObjectsOfTypeAll<Mesh>().Length).Append(", materials ").Append(Resources.FindObjectsOfTypeAll<Material>().Length).Append(", textures ").Append(Resources.FindObjectsOfTypeAll<Texture>().Length).Append('\n');
            SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
            float sum = 0f;
            for (int ch = 0; ch < 3; ch++) for (int k = 0; k < 9; k++) sum += probe[ch, k] * (1 + ch * 9 + k);
            say.Append("ambient ").Append(RenderSettings.ambientMode).Append(' ').Append(RenderSettings.ambientLight.ToString("F3")).Append(' ').Append(RenderSettings.ambientSkyColor.ToString("F3")).Append(' ').Append(RenderSettings.ambientEquatorColor.ToString("F3"))
               .Append(' ').Append(RenderSettings.ambientGroundColor.ToString("F3")).Append(' ').Append(RenderSettings.ambientIntensity.ToString("F3")).Append(" probe ").Append(sum.ToString("F4")).Append(" reflection ").Append(RenderSettings.defaultReflectionMode).Append(' ')
               .Append(RenderSettings.customReflection != null ? RenderSettings.customReflection.name : "none").Append(' ').Append(RenderSettings.reflectionIntensity.ToString("F3")).Append(" fog ").Append(RenderSettings.fog).Append('\n');
            say.Append("quality shadows ").Append(QualitySettings.shadows).Append(' ').Append(QualitySettings.shadowDistance.ToString("F1")).Append(' ').Append(QualitySettings.shadowCascades).Append('\n');
            say.Append("locks ").Append(InputLockManager.lockMask).Append(' ');
            var keys = new List<string>(InputLockManager.lockStack.Keys);
            keys.Sort(StringComparer.Ordinal);
            say.Append(string.Join(",", keys.ToArray())).Append('\n');
            say.Append("invert culling ").Append(GL.invertCulling).Append(", active render texture ").Append(RenderTexture.active != null ? RenderTexture.active.name : "none").Append('\n');
            return say.ToString();
        }
    }
}
#endif
