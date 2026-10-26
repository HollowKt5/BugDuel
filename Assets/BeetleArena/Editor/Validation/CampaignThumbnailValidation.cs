using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CampaignThumbnailValidation
{
    static readonly Type CaptureType = typeof(CampaignApp).Assembly.GetType("BeetleArena.Campaign.CampaignThumbnailCapture");
    static Task<string> Result(object capture) => (Task<string>)CaptureType.GetProperty("Result").GetValue(capture);

    [MenuItem("Tools/甲壳虫斗/验证/存档与数据/缩略图异步读回", false, 664)]
    public static void Run()
    {
        if (!SystemInfo.supportsAsyncGPUReadback) throw new Exception("GPU readback validation requires a supported graphics device.");
        var previousActive = RenderTexture.active;
        var frame = RenderTexture.GetTemporary(100, 180, 0, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(100, 180, TextureFormat.RGBA32, false);
        var reference = new Texture2D(2, 2);
        var actual = new Texture2D(2, 2);
        var host = new GameObject("Isolated thumbnail save"); host.SetActive(false);
        try
        {
            var pixels = new Color32[100 * 180];
            for (int y = 0; y < 180; y++)
                for (int x = 0; x < 100; x++) pixels[y * 100 + x] = y < 90 ? Color.red : Color.blue;
            texture.SetPixels32(pixels); texture.Apply(); Graphics.Blit(texture, frame);
            string sync = (string)CaptureType.GetMethod("CaptureSynchronously").Invoke(null, new object[] { frame });
            var capture = Activator.CreateInstance(CaptureType, new object[] { frame });
            var cancelled = Activator.CreateInstance(CaptureType, new object[] { frame });
            CaptureType.GetMethod("UseCachedThumbnail").Invoke(cancelled, null);
            Check(Result(cancelled).IsCompleted && Result(cancelled).Result == null, "pause releases pending save before GPU completion");
            // Exercise the actual save worker, with separate snapshots for the
            // completed and cancelled readbacks. Never access player storage.
            var app = host.AddComponent<CampaignApp>();
            var repository = new CampaignSaveRepository(Path.GetFullPath("Artifacts/Performance/ThumbnailSave-" + Guid.NewGuid().ToString("N")));
            const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(CampaignApp).GetField("repository", privateInstance).SetValue(app, repository);
            var session = CampaignSession.New(CampaignCatalog.Defaults(), 7351); session.RenamePlayer("Thumbnail test");
            var save = new CampaignSave { state = CampaignSnapshots.Copy(session.State), catalog = session.Catalog, thumbnail = "cached-image" };
            var cancelledSave = new CampaignSave { state = CampaignSnapshots.Copy(session.State), catalog = session.Catalog, thumbnail = "cached-image" };
            var write = typeof(CampaignApp).GetMethod("WriteSnapshot", privateInstance);
            var pending = (Task)write.Invoke(app, new object[] { 1, save, capture });
            var pendingCancelled = (Task)write.Invoke(app, new object[] { 2, cancelledSave, cancelled });
            // Waiting is confined to the regression harness, never the interactive save path.
            AsyncGPUReadback.WaitAllRequests();
            string image = Result(capture).GetAwaiter().GetResult();
            pending.GetAwaiter().GetResult(); pendingCancelled.GetAwaiter().GetResult();
            Check(repository.Read(1).thumbnail == image, "durable save waits for its own captured image");
            Check(repository.Read(2).thumbnail == "cached-image", "cancelled capture saves the matching cached image");
            Check(!string.IsNullOrEmpty(image), "readback produces JPEG data");
            Check(Result(cancelled).Result == null, "late GPU callback cannot replace a cancelled result");
            reference.LoadImage(Convert.FromBase64String(sync)); actual.LoadImage(Convert.FromBase64String(image));
            Check(actual.width == 320 && actual.height == 180, "thumbnail dimensions preserved");
            var expectedPixels = reference.GetPixels32(); var actualPixels = actual.GetPixels32();
            long difference = 0;
            for (int i = 0; i < expectedPixels.Length; i++)
            {
                difference += Math.Abs(expectedPixels[i].r - actualPixels[i].r);
                difference += Math.Abs(expectedPixels[i].g - actualPixels[i].g);
                difference += Math.Abs(expectedPixels[i].b - actualPixels[i].b);
            }
            double meanError = difference / (double)(expectedPixels.Length * 3);
            Check(meanError < 3, "orientation, color and letterboxing match synchronous capture (error=" + meanError + ")");
            Check(actual.GetPixel(5, 90).maxColorComponent < .05f, "narrow viewport keeps black side bars");
            Check(actual.GetPixel(160, 20).maxColorComponent > .8f, "capture contains rendered pixels");
            Directory.CreateDirectory("Artifacts/Performance");
            File.WriteAllBytes("Artifacts/Performance/Thumbnail.jpg", Convert.FromBase64String(image));
            File.WriteAllText("Artifacts/Performance/Thumbnail.txt", "PASS: JPEG size, orientation/color versus synchronous capture, side bars, cancellation, durable captured/cached image writes. Mean channel error=" + meanError);
        }
        finally
        {
            AsyncGPUReadback.WaitAllRequests();
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(frame);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(reference);
            UnityEngine.Object.DestroyImmediate(actual);
            UnityEngine.Object.DestroyImmediate(host);
        }
        Debug.Log("Campaign thumbnail readback PASS");
    }
    static void Check(bool passed, string message) { if (!passed) throw new Exception("Thumbnail: " + message); }
}
