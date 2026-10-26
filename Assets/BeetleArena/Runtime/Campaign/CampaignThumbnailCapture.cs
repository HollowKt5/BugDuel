using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace BeetleArena.Campaign
{
    // One capture belongs to one save snapshot. Completion never writes to live
    // campaign state, so a late GPU callback cannot replace another journey's image.
    internal sealed class CampaignThumbnailCapture
    {
        const int Width = 320, Height = 180;
        readonly TaskCompletionSource<string> completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> Result => completion.Task;

        public CampaignThumbnailCapture(RenderTexture frame)
        {
            if (!SystemInfo.supportsAsyncGPUReadback ||
                !SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormatUsage.ReadPixels))
            {
                completion.SetResult(CaptureSynchronously(frame));
                return;
            }
            var rt = DrawFrame(frame);
            try
            {
                AsyncGPUReadback.Request(rt, 0, GraphicsFormat.R8G8B8A8_UNorm, request =>
                {
                    try
                    {
                        if (request.hasError || completion.Task.IsCompleted) { UseCachedThumbnail(); return; }
                        // Request memory expires after the callback. The worker owns this copy.
                        Encode(request.GetData<byte>().ToArray());
                    }
                    catch (Exception error) { Debug.LogWarning("Thumbnail readback failed: " + error); UseCachedThumbnail(); }
                    finally { RenderTexture.ReleaseTemporary(rt); }
                });
            }
            catch
            {
                RenderTexture.ReleaseTemporary(rt);
                throw;
            }
        }

        async void Encode(byte[] pixels)
        {
            try
            {
                var image = await Task.Run(() => Convert.ToBase64String(ImageConversion.EncodeArrayToJPG(
                    pixels, GraphicsFormat.R8G8B8A8_UNorm, Width, Height, 0, 65))).ConfigureAwait(false);
                completion.TrySetResult(image);
            }
            catch (Exception error) { Debug.LogWarning("Thumbnail encoding failed: " + error); UseCachedThumbnail(); }
        }

        // Shutdown/pause may block on disk, but must not wait on a GPU callback which
        // needs the main loop. The request still owns its RT until its callback runs.
        public void UseCachedThumbnail() => completion.TrySetResult(null);

        public static string CaptureSynchronously(RenderTexture frame)
        {
            var rt = DrawFrame(frame); var old = RenderTexture.active; Texture2D texture = null;
            try
            {
                RenderTexture.active = rt;
                texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                return Convert.ToBase64String(texture.EncodeToJPG(65));
            }
            finally
            {
                RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt);
                if (texture)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
                    else UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        static RenderTexture DrawFrame(RenderTexture frame)
        {
            var rt = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32);
            var old = RenderTexture.active;
            try
            {
                RenderTexture.active = rt; GL.Clear(false, true, Color.black);
                float fit = Mathf.Min((float)Width / frame.width, (float)Height / frame.height);
                float width = frame.width * fit, height = frame.height * fit;
                GL.PushMatrix();
                try { GL.LoadPixelMatrix(0, Width, Height, 0); Graphics.DrawTexture(new Rect((Width - width) * .5f, (Height - height) * .5f, width, height), frame); }
                finally { GL.PopMatrix(); }
                return rt;
            }
            catch { RenderTexture.ReleaseTemporary(rt); throw; }
            finally { RenderTexture.active = old; }
        }
    }
}
