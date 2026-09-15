using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

static class PeepsAnalyticsSync
{
    static readonly Regex GameFolderJs = new Regex(
        @"var\s+GAME_FOLDER\s*=\s*['""][^'""]*['""]",
        RegexOptions.CultureInvariant);

    public static string ApplyAll(string gameFolder)
    {
        string folder = PeepsAnalyticsSettings.Sanitize(gameFolder);
        if (!PeepsAnalyticsSettings.IsValid(folder))
            return "Укажи имя папки игры на сервере, например MegaCarGame.";

        PeepsAnalyticsSettings.GameFolder = folder;

        int components = ApplyToLoadedComponents(folder);
        int prefabs = ApplyToPrefabs(folder);
        string jsPath = ApplyToFunnelJs(folder);
        string html = HtmlFunnelWebGLInstaller.ConnectFunnelToHtml(installJsIfMissing: true);

        string jsLine = string.IsNullOrEmpty(jsPath)
            ? "funnel.js не найден — поставь WebGL template или Peeps → Analytics → Install funnel.js."
            : "funnel.js: " + jsPath;

        return $"GAME_FOLDER = {folder}\n" +
               $"Компонентов в сценах: {components}\n" +
               $"Префабов: {prefabs}\n" +
               jsLine + "\n\n" +
               html;
    }

    public static void ApplyToGameObject(GameObject go)
    {
        string folder = PeepsAnalyticsSettings.GameFolder;
        if (!PeepsAnalyticsSettings.IsValid(folder) || go == null)
            return;
        foreach (var manager in go.GetComponents<AnalyticsManager>())
            SetGameFolder(manager, folder);
        foreach (var funnel in go.GetComponents<AnalyticsFunnel>())
            SetGameFolder(funnel, folder);
    }

    public static string GuessCurrentFolder()
    {
        string saved = PeepsAnalyticsSettings.GameFolder;
        if (PeepsAnalyticsSettings.IsValid(saved))
            return saved;

        string fromJs = ReadFolderFromFunnelJs();
        if (PeepsAnalyticsSettings.IsValid(fromJs))
            return fromJs;

        var managers = FindAll<AnalyticsManager>();
        foreach (var manager in managers)
        {
            if (manager != null && PeepsAnalyticsSettings.IsValid(manager.gameFolder))
                return manager.gameFolder;
        }

        var funnels = FindAll<AnalyticsFunnel>();
        foreach (var funnel in funnels)
        {
            if (funnel != null && PeepsAnalyticsSettings.IsValid(funnel.gameFolder))
                return funnel.gameFolder;
        }

        return saved;
    }

    static bool installingFunnelJs;

    public static string ApplyToFunnelJs(string folder)
    {
        string path = HtmlFunnelWebGLInstaller.FunnelJsPath;
        if ((string.IsNullOrEmpty(path) || !File.Exists(path)) && !installingFunnelJs)
        {
            installingFunnelJs = true;
            try { HtmlFunnelWebGLInstaller.Install(overwrite: false, logAlways: false); }
            finally { installingFunnelJs = false; }
            path = HtmlFunnelWebGLInstaller.FunnelJsPath;
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return "";

        string text = File.ReadAllText(path);
        string replacement = "var GAME_FOLDER = '" + folder.Replace("'", "") + "'";
        string next = GameFolderJs.IsMatch(text)
            ? GameFolderJs.Replace(text, replacement, 1)
            : replacement + ";\n" + text;

        if (next != text)
        {
            File.WriteAllText(path, next);
            AssetDatabase.Refresh();
        }

        return path;
    }

    static string ReadFolderFromFunnelJs()
    {
        string path = HtmlFunnelWebGLInstaller.FunnelJsPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return "";
        var match = Regex.Match(File.ReadAllText(path), @"var\s+GAME_FOLDER\s*=\s*['""]([^'""]+)['""]");
        return match.Success ? match.Groups[1].Value : "";
    }

    static int ApplyToLoadedComponents(string folder)
    {
        int count = 0;
        foreach (var manager in FindAll<AnalyticsManager>())
            count += SetGameFolder(manager, folder) ? 1 : 0;
        foreach (var funnel in FindAll<AnalyticsFunnel>())
            count += SetGameFolder(funnel, folder) ? 1 : 0;
        return count;
    }

    static int ApplyToPrefabs(string folder)
    {
        string managerGuid = ScriptGuid(typeof(AnalyticsManager));
        string funnelGuid = ScriptGuid(typeof(AnalyticsFunnel));
        int count = 0;

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        try
        {
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab"))
                    continue;
                if (!assetPath.StartsWith("Assets/") && assetPath.IndexOf("com.peeps.analytics") < 0)
                    continue;
                if (prefabGuids.Length > 40)
                    EditorUtility.DisplayProgressBar("Peeps Analytics", assetPath, (float)i / prefabGuids.Length);

                string yaml;
                try { yaml = File.ReadAllText(assetPath); }
                catch { continue; }

                bool hasManager = !string.IsNullOrEmpty(managerGuid) && yaml.Contains(managerGuid);
                bool hasFunnel = !string.IsNullOrEmpty(funnelGuid) && yaml.Contains(funnelGuid);
                if (!hasManager && !hasFunnel)
                    continue;

                GameObject root;
                try { root = PrefabUtility.LoadPrefabContents(assetPath); }
                catch { continue; }

                bool dirty = false;
                try
                {
                    foreach (var manager in root.GetComponentsInChildren<AnalyticsManager>(true))
                        dirty |= SetGameFolder(manager, folder);
                    foreach (var funnel in root.GetComponentsInChildren<AnalyticsFunnel>(true))
                        dirty |= SetGameFolder(funnel, folder);
                    if (dirty)
                    {
                        try
                        {
                            PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                            count++;
                        }
                        catch (System.Exception)
                        {
                            // Git UPM packages are often read-only; scene instances still get the value.
                        }
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        return count;
    }

    static bool SetGameFolder(Object obj, string folder)
    {
        if (obj == null)
            return false;
        var so = new SerializedObject(obj);
        SerializedProperty prop = so.FindProperty("gameFolder");
        if (prop == null || prop.stringValue == folder)
            return false;
        prop.stringValue = folder;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(obj);
        return true;
    }

    static T[] FindAll<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        return Object.FindObjectsOfType<T>(true);
#endif
    }

    static string ScriptGuid(System.Type type)
    {
        string[] guids = AssetDatabase.FindAssets(type.Name + " t:MonoScript");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null && script.GetClass() == type)
                return guid;
        }
        return "";
    }
}
