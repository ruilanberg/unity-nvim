using UnityEditor;
using UnityEngine;

namespace UnityNvim.Editor
{
    /// <summary>Only the user's main editor registers the code editor and opens the bridge (not import workers, batch runs or virtual players).</summary>
    public static class EditorProcess
    {
        public static bool IsMain
        {
            get
            {
                if (Application.isBatchMode)
                    return false;
#if UNITY_2020_2_OR_NEWER
                if (AssetDatabase.IsAssetImportWorkerProcess())
                    return false;
#endif
                return UnityEditor.MPE.ProcessService.level != UnityEditor.MPE.ProcessLevel.Secondary;
            }
        }
    }
}
