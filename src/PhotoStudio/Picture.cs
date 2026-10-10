using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PhotoStudio
{
    /// <summary>
    /// Pictures of the studio, larger than the screen, drawn afresh for the picture by the same cameras that draw the
    /// screen (so without the game's interface, the settings window or anything else drawn over the view), and saved as
    /// PNG in the game's own Screenshots folder.
    ///
    /// With a see-through background, the craft (and in the white studio its shadow) is drawn twice more, over black and
    /// over white, without the backdrop and without the game's post-processing; how much the two differ at each point is
    /// how see-through it is there, and its colour is what it is over black divided by how solid it is ("difference
    /// matting"). That needs nothing of the shaders (the parts' shaders do not all write a usable alpha), and gives soft
    /// edges, glass and the shadow their true transparency.
    /// </summary>
    static class Picture
    {
        public static string Folder => KSPUtil.ApplicationRootPath + "Screenshots" + Path.DirectorySeparatorChar;

        /// <summary>The largest side a picture may have here.</summary>
        static int Most => Mathf.Min(8192, SystemInfo.maxTextureSize);

        /// <summary>Take one: `times` the screen's size; see-through or not. Gives back where it was saved.</summary>
        public static string Take(Stage stage, int times, bool clear, string craftName, string studioName)
        {
            int w = Screen.width * Mathf.Max(1, times), h = Screen.height * Mathf.Max(1, times);
            if (Mathf.Max(w, h) > Most)
            {
                float k = Most / (float)Mathf.Max(w, h);
                w = Mathf.FloorToInt(w * k); h = Mathf.FloorToInt(h * k);
            }
            // (the edges smoothed by multisampling, fewer samples the larger the picture: the game's own camera draws it at that size too)
            int samples = w * h <= 1100000 ? 8 : w * h <= 4200000 ? 4 : w * h <= 17000000 ? 2 : 1;
            if (QualitySettings.antiAliasing == 0 && !stage.Eye.allowMSAA) samples = 1;

            Texture2D picture = null;
            RenderTexture drawn = null, flat = null;
            RenderTexture active = RenderTexture.active;
            try
            {
                drawn = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default) { antiAliasing = samples, name = "Photo Studio picture" };
                drawn.Create();
                flat = samples > 1 ? new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default) { name = "Photo Studio picture, read" } : drawn;
                if (samples > 1) flat.Create();
                if (!clear)
                {
                    Draw(stage.Back, drawn);
                    Draw(stage.Eye, drawn, stage);
                    picture = Read(drawn, flat, TextureFormat.RGB24);
                }
                else picture = Cut(stage, drawn, flat);
                byte[] png = picture.EncodeToPNG();
                Directory.CreateDirectory(Folder);
                string name = KSPUtil.SanitizeFilename((string.IsNullOrEmpty(craftName) ? "Craft" : craftName) + " - " + studioName + (clear ? " (clear)" : "") + " - " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
                string path = Folder + name + ".png";
                for (int n = 2; File.Exists(path); n++) path = Folder + name + " " + n + ".png";
                File.WriteAllBytes(path, png);
                return path;
            }
            finally
            {
                RenderTexture.active = active;
                if (picture != null) UnityEngine.Object.Destroy(picture);
                if (flat != null && flat != drawn) { flat.Release(); UnityEngine.Object.Destroy(flat); }
                if (drawn != null) { drawn.Release(); UnityEngine.Object.Destroy(drawn); }
            }
        }

        /// <summary>One camera's picture drawn into a target of ours, as it would be on the screen.</summary>
        static void Draw(Camera camera, RenderTexture into, Stage lensFor = null)
        {
            RenderTexture was = camera.targetTexture;
            try
            {
                camera.targetTexture = into;
                // (Keystone's lens works at the size the camera draws at: told again for the picture's, and for the screen's after)
                if (lensFor != null) lensFor.Focus();
                camera.Render();
            }
            finally
            {
                camera.targetTexture = was;
                if (lensFor != null) lensFor.Focus();
            }
        }

        static Texture2D Read(RenderTexture drawn, RenderTexture flat, TextureFormat format)
        {
            if (flat != drawn) Graphics.Blit(drawn, flat);
            var picture = new Texture2D(flat.width, flat.height, format, false);
            RenderTexture.active = flat;
            picture.ReadPixels(new Rect(0, 0, flat.width, flat.height), 0, 0, false);
            return picture;
        }

        /// <summary>The craft cut out of its background (see the class's own description).</summary>
        static Texture2D Cut(Stage stage, RenderTexture drawn, RenderTexture flat)
        {
            Camera eye = stage.Eye;
            var off = new List<Renderer>();
            var unprocessed = new List<Behaviour>();
            CameraClearFlags clearWas = eye.clearFlags;
            Color backWas = eye.backgroundColor;
            bool wideWas = eye.allowHDR;
            Color32[] overBlack, overWhite;
            try
            {
                foreach (Renderer r in stage.Backdrop) if (r != null && r.enabled) { r.enabled = false; off.Add(r); }
                foreach (Behaviour b in eye.GetComponents<Behaviour>())
                    if (b != null && b.enabled && b.GetType().Name == "PostProcessLayer") { b.enabled = false; unprocessed.Add(b); }
                eye.clearFlags = CameraClearFlags.SolidColor;
                eye.allowHDR = false;
                eye.backgroundColor = new Color(0f, 0f, 0f, 1f);
                Draw(eye, drawn, stage);
                Texture2D black = Read(drawn, flat, TextureFormat.RGBA32);
                overBlack = black.GetPixels32();
                UnityEngine.Object.Destroy(black);
                eye.backgroundColor = new Color(1f, 1f, 1f, 1f);
                Draw(eye, drawn, stage);
                Texture2D white = Read(drawn, flat, TextureFormat.RGBA32);
                overWhite = white.GetPixels32();
                UnityEngine.Object.Destroy(white);
            }
            finally
            {
                eye.clearFlags = clearWas; eye.backgroundColor = backWas; eye.allowHDR = wideWas;
                foreach (Behaviour b in unprocessed) if (b != null) b.enabled = true;
                foreach (Renderer r in off) if (r != null) r.enabled = true;
            }
            var cut = new Color32[overBlack.Length];
            for (int i = 0; i < cut.Length; i++)
            {
                Color32 b = overBlack[i], w = overWhite[i];
                // (how much of the white came through, the three colours taken together; the rest is how solid the picture is there)
                int through = (w.r - b.r) + (w.g - b.g) + (w.b - b.b);
                int a = 255 - (through + 1) / 3;
                if (a <= 0) { cut[i] = new Color32(0, 0, 0, 0); continue; }
                if (a > 255) a = 255;
                cut[i] = new Color32(Up(b.r, a), Up(b.g, a), Up(b.b, a), (byte)a);
            }
            var picture = new Texture2D(flat.width, flat.height, TextureFormat.RGBA32, false);
            picture.SetPixels32(cut);
            return picture;
        }

        /// <summary>A colour as it was over black, made whole again: divided by how solid it was.</summary>
        static byte Up(byte over, int solid)
        {
            int c = (over * 255 + solid / 2) / solid;
            return (byte)(c > 255 ? 255 : c);
        }
    }
}
