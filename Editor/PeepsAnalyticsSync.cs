using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

static class PeepsAnalyticsSync
{
    static readonly Regex GameFolderJs = new Regex(
        @"var\s+GAME_FOLDER\s*=\s*['""][^'""]*['""]",
        RegexOptions.CultureInvariant);

    static readonly Regex ServerHostJs = new Regex(
        @"var\s+SERVER_HOST\s*=\s*['""][^'""]*['""]",
        RegexOptions.CultureInvariant);

    public static string ApplyAll(string serverHost, string gameFolder)
    {
        string host = PeepsAnalyticsSettings.SanitizeHost(serverHost);
        string folder = PeepsAnalyticsSettings.SanitizeFolder(gameFolder);
        if (!PeepsAnalyticsSettings.IsValidHost(host))
            return "Укажи хост, например example.com.";
        if (!PeepsAnalyticsSettings.IsValidFolder(folder))
            return "Укажи имя папки игры на сервере, например НазваниеИгры.";

        PeepsAnalyticsSettings.Set(host, folder);

        int components = ApplyToLoadedComponents(host, folder);
        int prefabs = ApplyToPrefabs(host, folder);
        string jsPath = ApplyToFunnelJs(host, folder);
        string html = HtmlFunnelWebGLInstaller.ConnectFunnelToHtml(installJsIfMissing: true);

        string jsLine = string.IsNullOrEmpty(jsPath)
            ? "funnel.js не найден — поставь WebGL template или BetterAnalytics → Install funnel.js."
            : "funnel.js: " + jsPath;

        return $"SERVER_HOST = {host}\n" +
               $"GAME_FOLDER = {folder}\n" +
               $"Компонентов в сценах: {components}\n" +
               $"Префабов: {prefabs}\n" +
               jsLine + "\n\n" +
               html;
    }

    public static void ApplyToGameObject(GameObject go)
    {
        string host = PeepsAnalyticsSettings.ServerHost;
        string folder = PeepsAnalyticsSettings.GameFolder;
        if (!PeepsAnalyticsSettings.IsValidHost(host) ||
            !PeepsAnalyticsSettings.IsValidFolder(folder) ||
            go == null)
            return;
        foreach (var manager in go.GetComponents<AnalyticsManager>())
            SetEndpoint(manager, host, folder);
        foreach (var funnel in go.GetComponents<AnalyticsFunnel>())
            SetEndpoint(funnel, host, folder);
        foreach (var remote in go.GetComponents<RemoteConfigLoader>())
            SetEndpoint(remote, host, folder);
    }

    public static void ApplyRemoteFromAnalytics(UnityEngine.Object remote)
    {
        if (remote == null)
            return;

        string host = "";
        string folder = "";
        foreach (var obj in FindEndpointObjectsInScene("AnalyticsManager"))
        {
            if (obj == null)
                continue;
            var so = new SerializedObject(obj);
            SerializedProperty hostProp = so.FindProperty("serverBaseUrl") ?? so.FindProperty("serverHost");
            SerializedProperty folderProp = so.FindProperty("gameFolder");
            if (string.IsNullOrEmpty(host) && hostProp != null && !string.IsNullOrEmpty(hostProp.stringValue))
                host = PeepsAnalyticsSettings.SanitizeHost(hostProp.stringValue);
            if (string.IsNullOrEmpty(folder) && folderProp != null && PeepsAnalyticsSettings.IsValidFolder(folderProp.stringValue))
                folder = folderProp.stringValue;
        }

        if (!PeepsAnalyticsSettings.IsValidHost(host))
            host = PeepsAnalyticsSettings.ServerHost;
        if (!PeepsAnalyticsSettings.IsValidFolder(folder))
            folder = PeepsAnalyticsSettings.GameFolder;

        if (PeepsAnalyticsSettings.IsValidHost(host) && PeepsAnalyticsSettings.IsValidFolder(folder))
            SetEndpoint(remote, host, folder);
    }

    public static string GuessCurrentHost()
    {
        string saved = PeepsAnalyticsSettings.ServerHost;
        if (PeepsAnalyticsSettings.IsValidHost(saved))
            return saved;

        string fromJs = ReadHostFromFunnelJs();
        string jsHost = PeepsAnalyticsSettings.SanitizeHost(fromJs);
        if (PeepsAnalyticsSettings.IsValidHost(jsHost))
            return jsHost;

        foreach (var obj in FindEndpointObjectsInScene())
        {
            if (obj == null)
                continue;
            var so = new SerializedObject(obj);
            SerializedProperty hostProp = so.FindProperty("serverBaseUrl") ?? so.FindProperty("serverHost");
            if (hostProp == null)
                continue;
            string host = PeepsAnalyticsSettings.SanitizeHost(hostProp.stringValue);
            if (PeepsAnalyticsSettings.IsValidHost(host))
                return host;
        }

        return saved;
    }

    public static string GuessCurrentFolder()
    {
        string saved = PeepsAnalyticsSettings.GameFolder;
        if (PeepsAnalyticsSettings.IsValidFolder(saved))
            return saved;

        string fromJs = ReadFolderFromFunnelJs();
        if (PeepsAnalyticsSettings.IsValidFolder(fromJs))
            return fromJs;

        foreach (var obj in FindEndpointObjectsInScene())
        {
            if (obj == null)
                continue;
            var so = new SerializedObject(obj);
            SerializedProperty folderProp = so.FindProperty("gameFolder");
            if (folderProp != null && PeepsAnalyticsSettings.IsValidFolder(folderProp.stringValue))
                return folderProp.stringValue;
        }

        return saved;
    }

    static bool installingFunnelJs;

    public static string ApplyToFunnelJs(string host, string folder)
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
        string next = text;

        if (host != null)
        {
            string safeHost = host.Replace("'", "");
            string replacement = "var SERVER_HOST = '" + safeHost + "'";
            next = ServerHostJs.IsMatch(next)
                ? ServerHostJs.Replace(next, replacement, 1)
                : replacement + ";\n" + next;
        }

        if (folder != null)
        {
            string safeFolder = folder.Replace("'", "");
            string replacement = "var GAME_FOLDER = '" + safeFolder + "'";
            next = GameFolderJs.IsMatch(next)
                ? GameFolderJs.Replace(next, replacement, 1)
                : replacement + ";\n" + next;
        }

        if (next != text)
        {
            File.WriteAllText(path, next);
            AssetDatabase.Refresh();
        }

        return path;
    }

    static string ReadFolderFromFunnelJs()
    {
        return ReadJsString("GAME_FOLDER");
    }

    static string ReadHostFromFunnelJs()
    {
        return ReadJsString("SERVER_HOST");
    }

    static string ReadJsString(string name)
    {
        string path = HtmlFunnelWebGLInstaller.FunnelJsPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return "";
        var match = Regex.Match(
            File.ReadAllText(path),
            @"var\s+" + name + @"\s*=\s*['""]([^'""]*)['""]");
        return match.Success ? match.Groups[1].Value : "";
    }

    static readonly string[] EndpointTypeNames =
    {
        "AnalyticsManager",
        "AnalyticsFunnel",
        "RemoteConfigLoader"
    };

    static readonly string[] PrefabNameHints =
    {
        "Analytics",
        "SDK",
        "RemoteConfig",
        "Remote Config"
    };

    static int ApplyToLoadedComponents(string host, string folder)
    {
        int count = 0;
        foreach (var obj in FindEndpointObjectsInScene())
            count += SetEndpoint(obj, host, folder) ? 1 : 0;
        return count;
    }

    static int ApplyToPrefabs(string host, string folder)
    {
        var paths = CollectCandidatePrefabPaths();
        int count = 0;
        int i = 0;
        try
        {
            foreach (string assetPath in paths)
            {
                i++;
                if (paths.Count > 12)
                    EditorUtility.DisplayProgressBar("BetterAnalytics", assetPath, (float)i / paths.Count);

                GameObject root;
                try { root = PrefabUtility.LoadPrefabContents(assetPath); }
                catch { continue; }

                bool dirty = false;
                try
                {
                    foreach (var obj in FindEndpointObjectsOn(root))
                        dirty |= SetEndpoint(obj, host, folder);
                    if (dirty)
                    {
                        try
                        {
                            PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                            count++;
                        }
                        catch (Exception)
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

    static HashSet<string> CollectCandidatePrefabPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string hint in PrefabNameHints)
        {
            foreach (string guid in AssetDatabase.FindAssets(hint + " t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsProjectPrefab(path))
                    continue;
                string file = Path.GetFileNameWithoutExtension(path);
                if (file.IndexOf(hint.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) < 0 &&
                    file.IndexOf(hint, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                paths.Add(path);
            }
        }

        return paths;
    }

    static bool IsProjectPrefab(string path)
    {
        return !string.IsNullOrEmpty(path) &&
               path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
               (path.StartsWith("Assets/") || path.IndexOf("com.peeps.analytics", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public static IEnumerable<UnityEngine.Object> FindEndpointObjectsInScene(string typeName = null)
    {
        string[] names = string.IsNullOrEmpty(typeName) ? EndpointTypeNames : new[] { typeName };
        var seen = new HashSet<int>();
        foreach (string name in names)
        {
            foreach (Type type in FindTypesByName(name))
            {
                foreach (var obj in FindAll(type))
                {
                    if (obj == null)
                        continue;
                    int id = obj.GetInstanceID();
                    if (!seen.Add(id))
                        continue;
                    yield return obj;
                }
            }
        }
    }

    static IEnumerable<UnityEngine.Object> FindEndpointObjectsOn(GameObject root)
    {
        if (root == null)
            yield break;
        foreach (string name in EndpointTypeNames)
        {
            foreach (Type type in FindTypesByName(name))
            {
                var found = root.GetComponentsInChildren(type, true);
                if (found == null)
                    continue;
                foreach (var obj in found)
                {
                    if (obj != null)
                        yield return obj;
                }
            }
        }
    }

    public static IEnumerable<Type> FindTypesByName(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try { type = assembly.GetType(typeName); }
            catch { continue; }
            if (type != null && typeof(UnityEngine.Object).IsAssignableFrom(type))
                yield return type;
        }
    }

    static bool SetEndpoint(UnityEngine.Object obj, string host, string folder)
    {
        if (obj == null)
            return false;
        var so = new SerializedObject(obj);
        bool dirty = false;
        SerializedProperty hostProp = so.FindProperty("serverBaseUrl");
        if (hostProp == null)
            hostProp = so.FindProperty("serverHost");
        SerializedProperty folderProp = so.FindProperty("gameFolder");
        if (hostProp != null && hostProp.stringValue != host)
        {
            hostProp.stringValue = host;
            dirty = true;
        }
        if (folderProp != null && folderProp.stringValue != folder)
        {
            folderProp.stringValue = folder;
            dirty = true;
        }
        if (!dirty)
            return false;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(obj);
        return true;
    }

    static UnityEngine.Object[] FindAll(Type type)
    {
        if (type == null)
            return Array.Empty<UnityEngine.Object>();
#if UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        return UnityEngine.Object.FindObjectsOfType(type, true);
#endif
    }
}
