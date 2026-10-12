using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UnityNvim.Editor.Bridge
{
    /// <summary>Library/UnityNvim/endpoint.json: how nvim finds this editor (port + token). Library/ is never committed.</summary>
    public static class EndpointFile
    {
        public static string Directory => Path.Combine(ProjectInfo.Root, "Library", "UnityNvim");
        public static string FilePath => Path.Combine(Directory, "endpoint.json");

        public static void Write(int port, string token)
        {
            var data = new Dictionary<string, object>
            {
                ["port"] = port,
                ["token"] = token,
                ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id,
                ["project"] = ProjectInfo.Root,
                ["projectName"] = ProjectInfo.Name,
                ["solution"] = ProjectInfo.SolutionPath,
                ["unityVersion"] = Application.unityVersion,
                ["nvimAddress"] = ProjectInfo.NvimAddress,
                ["protocol"] = Protocol.Version,
            };
            System.IO.Directory.CreateDirectory(Directory);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(data));
            if (File.Exists(FilePath))
                File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch (IOException)
            {
                // nvim may be reading it; it will just fail to connect
            }
        }
    }
}
