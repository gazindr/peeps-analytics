using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Copies funnel.js into the project's WebGL template after the package is added.
/// </summary>
public static class HtmlFunnelWebGLInstaller
{
    const string MenuRoot = "Peeps/Analytics/";

    [MenuItem(MenuRoot + "Add Analytics object to scene")]
    public static void AddAnalyticsToScene()
    {
        var go = new GameObject("Analytics");
        go.AddComponent<AnalyticsManager>();
        go.AddComponent<AnalyticsFunnel>();
        Undo.RegisterCreatedObjectUndo(go, "Add Analytics");
        Selection.activeGameObject = go;
    }

    [MenuItem(MenuRoot + "Install funnel.js into WebGL template")]
    public static void InstallFromMenu()
    {
        Install(overwrite: false, logAlways: true);
    }

    [MenuItem(MenuRoot + "Reinstall funnel.js (overwrite)")]
    public static void ReinstallFromMenu()
    {
        Install(overwrite: true, logAlways: true);
    }

    [InitializeOnLoadMethod]
    static void AutoInstallOnce()
    {
        if (SessionState.GetBool("PeepsAnalytics.FunnelInstallChecked", false))
            return;
        SessionState.SetBool("PeepsAnalytics.FunnelInstallChecked", true);
        EditorApplication.delayCall += () => Install(overwrite: false, logAlways: false);
    }

    static void Install(bool overwrite, bool logAlways)
    {
        string src = Path.Combine(PackageRoot(), "Editor", "FunnelTemplate", "funnel.js.txt");
        if (!File.Exists(src))
        {
            if (logAlways)
                Debug.LogWarning("[Peeps Analytics] funnel.js template is missing in the package.");
            return;
        }

        string templateDir = ResolveTemplateDir();
        Directory.CreateDirectory(templateDir);
        string dst = Path.Combine(templateDir, "funnel.js");

        if (File.Exists(dst) && !overwrite)
        {
            PatchIndexHtml(templateDir, logAlways);
            if (logAlways)
                Debug.Log("[Peeps Analytics] funnel.js already exists. Use Reinstall to overwrite.\n" + dst);
            return;
        }

        File.Copy(src, dst, overwrite: true);
        PatchIndexHtml(templateDir, logAlways: true);
        Debug.Log("[Peeps Analytics] Installed funnel.js. Set GAME_FOLDER inside it to your server folder.\n" + dst);
    }

    static string ResolveTemplateDir()
    {
        string template = PlayerSettings.WebGL.template ?? "";
        if (template.StartsWith("PROJECT:"))
        {
            string name = template.Substring("PROJECT:".Length);
            if (!string.IsNullOrEmpty(name))
                return Path.Combine(Application.dataPath, "WebGLTemplates", name);
        }

        string bridge = Path.Combine(Application.dataPath, "WebGLTemplates", "Bridge");
        if (Directory.Exists(bridge))
            return bridge;

        return Path.Combine(Application.dataPath, "WebGLTemplates", "Bridge");
    }

    static void PatchIndexHtml(string templateDir, bool logAlways)
    {
        string indexPath = Path.Combine(templateDir, "index.html");
        if (!File.Exists(indexPath))
        {
            if (logAlways)
            {
                Debug.LogWarning(
                    "[Peeps Analytics] No index.html in the WebGL template. Add this line in <head>:\n" +
                    "        <script src=\"./funnel.js\"></script>");
            }
            return;
        }

        string html = File.ReadAllText(indexPath);
        if (Regex.IsMatch(html, @"funnel\.js", RegexOptions.IgnoreCase))
            return;

        string tag = "        <script src=\"./funnel.js\"></script>";
        var head = new Regex("<head[^>]*>", RegexOptions.IgnoreCase);
        if (head.IsMatch(html))
        {
            html = head.Replace(html, m => m.Value + "\n" + tag, 1);
            File.WriteAllText(indexPath, html);
            Debug.Log("[Peeps Analytics] Added funnel.js script tag to " + indexPath);
            return;
        }

        if (logAlways)
            Debug.LogWarning("[Peeps Analytics] Could not find <head> in index.html. Add:\n" + tag);
    }

    static string PackageRoot()
    {
        var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(HtmlFunnelWebGLInstaller).Assembly);
        if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
            return info.resolvedPath;
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
