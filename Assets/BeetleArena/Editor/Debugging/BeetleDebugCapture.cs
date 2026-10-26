using System.IO;
using System.Linq;
using UnityEngine;

namespace BeetleArena.EditorTools
{
    /// <summary>Outputs actual camera frame for a bug scene. Temporarily isolates render layer; finally restores all objects and camera properties.</summary>
    public static class BeetleDebugCapture
    {
        public static string Save(BeetleMatchController match,string path)
        {
            var camera=match.spectatorCamera.GetComponent<Camera>();
            var own=match.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
            var layers=own.Select(t=>t.gameObject.layer).ToArray();int mask=camera.cullingMask;
            // Unity's internal hidden preview objects may reside on the same layer, so external Renderers are excluded by scene membership.
            var others=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(r=>r.gameObject.scene!=match.gameObject.scene && r.enabled).ToArray();
            var lights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(l=>l.gameObject.scene!=match.gameObject.scene && l.enabled).ToArray();
            var target=new RenderTexture(1280,720,24);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
            var old=camera.targetTexture;var active=RenderTexture.active;
            try
            {
                foreach(var t in own)t.gameObject.layer=31;foreach(var r in others)r.enabled=false;foreach(var l in lights)l.enabled=false;
                camera.cullingMask=1<<31;camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();
                path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,pixels.EncodeToPNG());return path;
            }
            finally
            {
                camera.targetTexture=old;camera.cullingMask=mask;RenderTexture.active=active;
                for(int i=0;i<own.Length;i++)if(own[i])own[i].gameObject.layer=layers[i];
                foreach(var r in others)if(r)r.enabled=true;foreach(var l in lights)if(l)l.enabled=true;
                UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}
