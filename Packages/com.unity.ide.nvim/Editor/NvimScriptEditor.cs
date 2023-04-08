using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Unity.CodeEditor;

namespace NvimEditor
{
    [InitializeOnLoad]
    public class NvimScriptEditor : IExternalCodeEditor
    {
        private const string NvimArgument = "nvim_arguments";
        private const string NvimExtension = "nvim_userExtensions";
        private static readonly GUIContent ResetArguments =
            EditorGUIUtility.TrTextContent("Reset argument");

        private string _arguments;

        private readonly IDiscovery _discoverability;
        private readonly IGenerator _projectGeneration;

        private static readonly string[] SupportedFileNames = { "nvim" };

        private static bool IsOsx => Application.platform == RuntimePlatform.OSXEditor;

        private static string DefaultApp => EditorPrefs.GetString("kScriptsDefaultApp");

        /*private static string DefaultArgument {DefaultArgument get; } =
            "\"$(ProjectPath)\" -g \"$(File)\":$(Line):$(Column)";*/

        private static string DefaultArgument =>
            "--server /tmp/nvim.unity --remote {0} --remote-send ':{1}<CR>'";

        private string Arguments
        {
            get => _arguments ??= EditorPrefs.GetString(NvimArgument, DefaultArgument);
            set
            {
                _arguments = value;
                EditorPrefs.SetString(NvimArgument, value);
            }
        }

        private static string[] DefaultExtensions
        {
            get
            {
                var customExtensions = new[] { "json", "asmdef", "log" };
                return EditorSettings.projectGenerationBuiltinExtensions
                    .Concat(EditorSettings.projectGenerationUserExtensions)
                    .Concat(customExtensions)
                    .Distinct().ToArray();
            }
        }

        private static string[] HandledExtensions
        {
            get
            {
                return HandledExtensionsString
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.TrimStart('.', '*'))
                    .ToArray();
            }
        }

        private static string HandledExtensionsString
        {
            get => EditorPrefs.GetString(NvimExtension, string.Join(";", DefaultExtensions));
            set => EditorPrefs.SetString(NvimExtension, value);
        }

        public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation)
        {
            var lowerCasePath = editorPath.ToLower();
            var filename = Path.GetFileName(lowerCasePath).Replace(" ", "");
            var installations = Installations;
            if (!SupportedFileNames.Contains(filename))
            {
                installation = default;
                return false;
            }

            if (!installations.Any())
            {
                installation = new CodeEditor.Installation
                {
                    Name = "Nvim",
                    Path = editorPath
                };
            }
            else
            {
                try
                {
                    installation = installations.First(inst => inst.Path == editorPath);
                }
                catch (InvalidOperationException)
                {
                    installation = new CodeEditor.Installation
                    {
                        Name = "Nvim",
                        Path = editorPath
                    };
                }
            }

            return true;
        }

        public void OnGUI()
        {
            Arguments = EditorGUILayout.TextField("External Script Editor Args", Arguments);
            if (GUILayout.Button(ResetArguments, GUILayout.Width(120)))
            {
                Arguments = DefaultArgument;
            }

            EditorGUILayout.LabelField("Generate .csproj files for:");
            EditorGUI.indentLevel++;
            SettingsButton(ProjectGenerationFlag.Embedded, "Embedded packages", "");
            SettingsButton(ProjectGenerationFlag.Local, "Local packages", "");
            SettingsButton(ProjectGenerationFlag.Registry, "Registry packages", "");
            SettingsButton(ProjectGenerationFlag.Git, "Git packages", "");
            SettingsButton(ProjectGenerationFlag.BuiltIn, "Built-in packages", "");
#if UNITY_2019_3_OR_NEWER
            SettingsButton(ProjectGenerationFlag.LocalTarBall, "Local tarball", "");
#endif
            SettingsButton(ProjectGenerationFlag.Unknown, "Packages from unknown sources", "");
            RegenerateProjectFiles();
            EditorGUI.indentLevel--;

            HandledExtensionsString = EditorGUILayout.TextField(new GUIContent("Extensions handled: "), HandledExtensionsString);
        }

        private void RegenerateProjectFiles()
        {
            var rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(new GUILayoutOption[] { }));
            rect.width = 252;
            if (GUI.Button(rect, "Regenerate project files"))
            {
                _projectGeneration.Sync();
            }
        }

        private void SettingsButton(ProjectGenerationFlag preference, string guiMessage, string toolTip)
        {
            var prevValue = _projectGeneration.AssemblyNameProvider.ProjectGenerationFlag.HasFlag(preference);
            var newValue = EditorGUILayout.Toggle(new GUIContent(guiMessage, toolTip), prevValue);
            if (newValue != prevValue)
            {
                _projectGeneration.AssemblyNameProvider.ToggleProjectGeneration(preference);
            }
        }

        public void CreateIfDoesNotExist()
        {
            if (!_projectGeneration.SolutionExists())
            {
                _projectGeneration.Sync();
            }
        }

        public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles, string[] importedFiles)
        {
            (_projectGeneration.AssemblyNameProvider as IPackageInfoCache)?.ResetPackageInfoCache();
            _projectGeneration.SyncIfNeeded(addedFiles.Union(deletedFiles).Union(movedFiles).Union(movedFromFiles).ToList(), importedFiles);
        }

        public void SyncAll()
        {
            (_projectGeneration.AssemblyNameProvider as IPackageInfoCache)?.ResetPackageInfoCache();
            AssetDatabase.Refresh();
            _projectGeneration.Sync();
        }

        public bool OpenProject(string path, int line, int column)
        {
            if (path != "" && (!SupportsExtension(path) || !File.Exists(path))) // Assets - Open C# Project passes empty path here
            {
                return false;
            }

            if (line == -1)
                line = 1;
            if (column == -1)
                column = 0;

            var arguments = string.Format(DefaultArgument, path, line);
            
            /*if (Arguments != DefaultArgument)
            {
                arguments = _projectGeneration.ProjectDirectory != path
                    ? CodeEditor.ParseArgument(Arguments, path, line, column)
                    : _projectGeneration.ProjectDirectory;
            }
            else
            {
                arguments = $@"""{_projectGeneration.ProjectDirectory}""";
                if (_projectGeneration.ProjectDirectory != path && path.Length != 0)
                {
                    arguments += $@" -g ""{path}"":{line}:{column}";
                }
            }*/

            if (IsOsx)
            {
                return OpenOsX(arguments);
            }

            var app = DefaultApp;
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = app,
                    Arguments = arguments,
                    WindowStyle = app.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
                    CreateNoWindow = true,
                    UseShellExecute = true,
                }
            };

            process.Start();
            return true;
        }

        private static bool OpenOsX(string arguments)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-n \"{DefaultApp}\" --args {arguments}",
                    UseShellExecute = true,
                }
            };

            process.Start();
            return true;
        }

        private static bool SupportsExtension(string path)
        {
            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
                return false;
            return HandledExtensions.Contains(extension.TrimStart('.'));
        }

        public CodeEditor.Installation[] Installations => _discoverability.PathCallback();

        public NvimScriptEditor(IDiscovery discovery, IGenerator projectGeneration)
        {
            _discoverability = discovery;
            _projectGeneration = projectGeneration;
        }

        static NvimScriptEditor()
        {
            var editor = new NvimScriptEditor(
                new NvimDiscovery(), new ProjectGeneration(Directory.GetParent(Application.dataPath)?.FullName));
            CodeEditor.Register(editor);

            if (IsNvimInstallation(CodeEditor.CurrentEditorInstallation))
            {
                editor.CreateIfDoesNotExist();
            }
        }

        private static bool IsNvimInstallation(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var lowerCasePath = path.ToLower();
            var filename = Path
                .GetFileName(lowerCasePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar))
                .Replace(" ", "");
            return SupportedFileNames.Contains(filename);
        }

        public void Initialize(string editorInstallationPath) { }
    }
}
