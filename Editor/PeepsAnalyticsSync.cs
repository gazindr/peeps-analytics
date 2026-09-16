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

    public static string GuessCurrentHost()
    {
        string saved = PeepsAnalyticsSettings.ServerHost;
        if (PeepsAnalyticsSettings.IsValidHost(saved))
            return saved;

        string fromJs = ReadHostFromFunnelJs();
        string jsHost = PeepsAnalyticsSettings.SanitizeHost(fromJs);
        if (PeepsAnalyticsSettings.IsValidHost(jsHost))
            return jsHost;

        foreach (var manager in FindAll<AnalyticsManager>())
        {
            if (manager == null)
                continue;
            string host = PeepsAnalyticsSettings.SanitizeHost(manager.serverBaseUrl);
            if (PeepsAnalyticsSettings.IsValidHost(host))
                return host;
        }

        foreach (var funnel in FindAll<AnalyticsFunnel>())
        {
            if (funnel == null)
                continue;
            string host = PeepsAnalyticsSettings.SanitizeHost(funnel.serverBaseUrl);
            if (PeepsAnalyticsSettings.IsValidHost(host))
                return host;
        }

        foreach (var remote in FindAll<RemoteConfigLoader>())
        {
            if (remote == null)
                continue;
            string host = PeepsAnalyticsSettings.SanitizeHost(remote.serverBaseUrl);
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

        foreach (var manager in FindAll<AnalyticsManager>())
        {
            if (manager != null && PeepsAnalyticsSettings.IsValidFolder(manager.gameFolder))
                return manager.gameFolder;
        }

        foreach (var funnel in FindAll<AnalyticsFunnel>())
        {
            if (funnel != null && PeepsAnalyticsSettings.IsValidFolder(funnel.gameFolder))
                return funnel.gameFolder;
        }

        foreach (var remote in FindAll<RemoteConfigLoader>())
        {
            if (remote != null && PeepsAnalyticsSettings.IsValidFolder(remote.gameFolder))
                return remote.gameFolder;
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

    static int ApplyToLoadedComponents(string host, string folder)
    {
        int count = 0;
        foreach (var manager in FindAll<AnalyticsManager>())
            count += SetEndpoint(manager, host, folder) ? 1 : 0;
        foreach (var funnel in FindAll<AnalyticsFunnel>())
            count += SetEndpoint(funnel, host, folder) ? 1 : 0;
        foreach (var remote in FindAll<RemoteConfigLoader>())
            count += SetEndpoint(remote, host, folder) ? 1 : 0;
        return count;
    }

    static int ApplyToPrefabs(string host, string folder)
    {
        string managerGuid = ScriptGuid(typeof(AnalyticsManager));
        string funnelGuid = ScriptGuid(typeof(AnalyticsFunnel));
        string remoteGuid = ScriptGuid(typeof(RemoteConfigLoader));
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
                    EditorUtility.DisplayProgressBar("BetterAnalytics", assetPath, (float)i / prefabGuids.Length);

                string yaml;
                try { yaml = File.ReadAllText(assetPath); }
                catch { continue; }

                bool hasManager = !string.IsNullOrEmpty(managerGuid) && yaml.Contains(managerGuid);
                bool hasFunnel = !string.IsNullOrEmpty(funnelGuid) && yaml.Contains(funnelGuid);
                bool hasRemote = !string.IsNullOrEmpty(remoteGuid) && yaml.Contains(remoteGuid);
                if (!hasManager && !hasFunnel && !hasRemote)
                    continue;

                GameObject root;
                try { root = PrefabUtility.LoadPrefabContents(assetPath); }
                catch { continue; }

                bool dirty = false;
                try
                {
                    foreach (var manager in root.GetComponentsInChildren<AnalyticsManager>(true))
                        dirty |= SetEndpoint(manager, host, folder);
                    foreach (var funnel in root.GetComponentsInChildren<AnalyticsFunnel>(true))
                        dirty |= SetEndpoint(funnel, host, folder);
                    foreach (var remote in root.GetComponentsInChildren<RemoteConfigLoader>(true))
                        dirty |= SetEndpoint(remote, host, folder);
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

    static bool SetEndpoint(Object obj, string host, string folder)
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
