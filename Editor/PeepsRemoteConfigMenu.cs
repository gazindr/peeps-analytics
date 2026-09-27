using System;
using System.IO;
using UnityEditor;
using UnityEngine;

static class PeepsRemoteConfigMenu
{
    const string ActionsAssetPath = "Assets/BetterAnalytics/RemoteConfigActions.cs";

    public static void AddToScene()
    {
        EnsureActionsScript();
        if (FindActionsType() != null)
        {
            FinishAddToScene();
            return;
        }
        if (File.Exists(ActionsAssetPath) || EditorApplication.isCompiling)
        {
            WhenActionsReady(FinishAddToScene);
            return;
        }
        FinishAddToScene();
    }

    static void FinishAddToScene()
    {
        Component loader = FindExistingLoader();
        GameObject go;
        if (loader != null)
        {
            go = loader.gameObject;
        }
        else
        {
            go = new GameObject("RemoteConfig");
            loader = go.AddComponent<RemoteConfigLoader>();
            Undo.RegisterCreatedObjectUndo(go, "Add Remote Config");
        }

        PeepsAnalyticsSync.ApplyRemoteFromAnalytics(loader);
        AddActionsComponent(go);
        Selection.activeGameObject = go;
        var so = new SerializedObject(loader);
        string host = (so.FindProperty("serverBaseUrl") ?? so.FindProperty("serverHost"))?.stringValue;
        string folder = so.FindProperty("gameFolder")?.stringValue;
        Debug.Log("[BetterAnalytics] Remote Config on " + go.name +
                  " host=" + host +
                  " folder=" + folder);
    }

    static bool EnsureActionsScript()
    {
        if (FindActionsType() != null)
            return false;
        if (File.Exists(ActionsAssetPath))
            return true;

        string template = Path.Combine(PackageRoot(), "Editor", "Templates", "RemoteConfigActions.cs.txt");
        if (!File.Exists(template))
        {
            Debug.LogWarning("[BetterAnalytics] RemoteConfigActions template is missing.");
            return false;
        }

        string dir = Path.GetDirectoryName(ActionsAssetPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.Copy(template, ActionsAssetPath, overwrite: false);
        AssetDatabase.Refresh();
        Debug.Log("[BetterAnalytics] Created " + ActionsAssetPath);
        return true;
    }

    static void AddActionsComponent(GameObject go)
    {
        Type type = FindActionsType();
        if (type == null || go.GetComponent(type) != null)
            return;
        Undo.AddComponent(go, type);
    }

    static Component FindExistingLoader()
    {
        Component fallback = null;
        foreach (var obj in PeepsAnalyticsSync.FindEndpointObjectsInScene("RemoteConfigLoader"))
        {
            var component = obj as Component;
            if (component == null)
                continue;
            if (string.Equals(component.gameObject.name, "SDK", StringComparison.OrdinalIgnoreCase))
                return component;
            if (fallback == null)
                fallback = component;
        }
        return fallback;
    }

    static Type FindActionsType()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try { type = assembly.GetType("RemoteConfigActions"); }
            catch { continue; }
            if (type != null && typeof(MonoBehaviour).IsAssignableFrom(type))
                return type;
        }
        return null;
    }

    static void WhenActionsReady(Action action)
    {
        double started = EditorApplication.timeSinceStartup;
        void Tick()
        {
            bool timeout = EditorApplication.timeSinceStartup - started > 30;
            if (!timeout && (EditorApplication.isCompiling || FindActionsType() == null))
                return;
            EditorApplication.update -= Tick;
            action();
        }

        EditorApplication.update += Tick;
    }

    static string PackageRoot()
    {
        var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PeepsRemoteConfigMenu).Assembly);
        if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
            return info.resolvedPath;
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
