using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.CodeEditor;

namespace NvimEditor
{
    public interface IDiscovery
    {
        CodeEditor.Installation[] PathCallback();
    }

    public class NvimDiscovery : IDiscovery
    {
        private List<CodeEditor.Installation> _installations;

        public CodeEditor.Installation[] PathCallback()
        {
            if (_installations != null)
                return _installations.ToArray();
            
            _installations = new List<CodeEditor.Installation>();
            FindInstallationPaths();
            return _installations.ToArray();
        }

        private void FindInstallationPaths()
        {
            string[] possiblePaths =
#if UNITY_EDITOR_OSX
// TODO: Need to handle for osx and windows
            {
                "/Applications/Visual Studio Code.app",
                "/Applications/Visual Studio Code - Insiders.app"
            };
#elif UNITY_EDITOR_WIN
            {
                GetProgramFiles() + @"/Microsoft VS Code/bin/code.cmd",
                GetProgramFiles() + @"/Microsoft VS Code/Code.exe",
                GetProgramFiles() + @"/Microsoft VS Code Insiders/bin/code-insiders.cmd",
                GetProgramFiles() + @"/Microsoft VS Code Insiders/Code.exe",
                GetLocalAppData() + @"/Programs/Microsoft VS Code/bin/code.cmd",
                GetLocalAppData() + @"/Programs/Microsoft VS Code/Code.exe",
                GetLocalAppData() + @"/Programs/Microsoft VS Code Insiders/bin/code-insiders.cmd",
                GetLocalAppData() + @"/Programs/Microsoft VS Code Insiders/Code.exe",
            };
#else
            {
                "/usr/local/bin/nvim",
            };
#endif
            var existingPaths = possiblePaths.Where(NvimExists).ToList();
            if (!existingPaths.Any())
            {
                return;
            }

            var lcp = GetLongestCommonPrefix(existingPaths);
            switch (existingPaths.Count)
            {
                case 1:
                {
                    var path = existingPaths.First();
                    _installations = new List<CodeEditor.Installation>
                    {
                        new CodeEditor.Installation { Path = path, Name = "Nvim" }
                    };
                    break;
                }
                case 2 when existingPaths.Any(path => !(path.Substring(lcp.Length).Contains("/") || path.Substring(lcp.Length).Contains("\\"))):
                {
                    goto case 1;
                }
                default:
                {
                    _installations = existingPaths.Select(path => new CodeEditor.Installation
                    {
                        Name = $"Nvim ({path.Substring(lcp.Length)})",
                        Path = path
                    }).ToList();

                    break;
                }
            }
        }

#if UNITY_EDITOR_WIN
        static string GetProgramFiles()
        {
            return Environment.GetEnvironmentVariable("ProgramFiles")?.Replace("\\", "/");
        }

        static string GetLocalAppData()
        {
            return Environment.GetEnvironmentVariable("LOCALAPPDATA")?.Replace("\\", "/");
        }
#endif

        private static string GetLongestCommonPrefix(IReadOnlyList<string> paths)
        {
            var baseLength = paths.First().Length;
            for (var pathIndex = 1; pathIndex < paths.Count; pathIndex++)
            {
                baseLength = Math.Min(baseLength, paths[pathIndex].Length);
                for (var i = 0; i < baseLength; i++)
                {
                    if (paths[pathIndex][i] == paths[0][i]) continue;

                    baseLength = i;
                    break;
                }
            }

            return paths[0].Substring(0, baseLength);
        }

        private static bool NvimExists(string path)
        {
#if UNITY_EDITOR_OSX
            return System.IO.Directory.Exists(path);
#else
            return new FileInfo(path).Exists;
#endif
        }
    }
}
