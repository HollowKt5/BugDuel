using System.IO;
using UnityEngine;

namespace BeetleArena
{
    public static class BeetleStoragePaths
    {
        // Product branding may change; the established Windows save location must not.
        public static string PersistentData
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return Path.Combine(Directory.GetParent(Application.persistentDataPath).FullName,"BugDuel");
#else
                return Application.persistentDataPath;
#endif
            }
        }
    }
}
