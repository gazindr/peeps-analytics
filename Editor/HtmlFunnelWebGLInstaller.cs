using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Copies funnel.js into the project's WebGL template after the package is added.
/// </summary>
public static class HtmlFunnelWebGLInstaller
{
    public const string MenuRoot = "BetterAnalytics/";

    public static string FunnelJsPath
    {
        get
        {
            string dir = ResolveTemplateDir();
            return string.IsNullOrEmpty(dir) ? "" : Path.Combine(dir, "funnel.js");
        }
    }

    [MenuItem(MenuRoot + "Add Analytics object to scene", false, 20)]
    public static void AddAnalyticsToScene()
    {
        var go = new GameObject("Analytics");
        go.AddComponent<AnalyticsManager>();
        go.AddComponent<AnalyticsFunnel>();
        PeepsAnalyticsSync.ApplyToGameObject(go);
        Undo.RegisterCreatedObjectUndo(go, "Add Analytics");
        Selection.activeGameObject = go;
    }

    [MenuItem(MenuRoot + "Install funnel.js into WebGL template", false, 21)]
    public static void InstallFromMenu()
    {
        Install(overwrite: false, logAlways: true);
    }

    [MenuItem(MenuRoot + "Reinstall funnel.js (overwrite)", false, 22)]
    public static void ReinstallFromMenu()
    {
        Install(overwrite: true, logAlways: true);
    }

    [MenuItem(MenuRoot + "Connect funnel.js in HTML", false, 23)]
    public static void ConnectHtmlFromMenu()
    {
        string result = ConnectFunnelToHtml(installJsIfMissing: true);
        Debug.Log("[BetterAnalytics]\n" + result);
        EditorUtility.DisplayDialog("BetterAnalytics", result, "OK");
    }

    [InitializeOnLoadMethod]
    static void AutoInstallOnce()
    {
        if (SessionState.GetBool("BetterAnalytics.FunnelInstallChecked", false) ||
            SessionState.GetBool("PeepsAnalytics.FunnelInstallChecked", false))
            return;
        SessionState.SetBool("BetterAnalytics.FunnelInstallChecked", true);
        EditorApplication.delayCall += () => Install(overwrite: false, logAlways: false);
    }

    public static void Install(bool overwrite, bool logAlways)
    {
        string src = Path.Combine(PackageRoot(), "Editor", "FunnelTemplate", "funnel.js.txt");
        if (!File.Exists(src))
        {
            if (logAlways)
                Debug.LogWarning("[BetterAnalytics] funnel.js template is missing in the package.");
            return;
        }

        string templateDir = ResolveTemplateDir();
        Directory.CreateDirectory(templateDir);
        string dst = Path.Combine(templateDir, "funnel.js");

        if (File.Exists(dst) && !overwrite)
        {
            ConnectFunnelToHtml(installJsIfMissing: false);
            ApplySavedFolderToFunnelJs();
            if (logAlways)
                Debug.Log("[BetterAnalytics] funnel.js already exists. Use Reinstall to overwrite.\n" + dst);
            return;
        }

        File.Copy(src, dst, overwrite: true);
        ConnectFunnelToHtml(installJsIfMissing: false);
        ApplySavedFolderToFunnelJs();
        Debug.Log("[BetterAnalytics] Installed funnel.js. Set Host and Game Folder via BetterAnalytics → Game Folder.\n" + dst);
    }

    public static string IndexHtmlPath
    {
        get
        {
            string dir = ResolveTemplateDir();
            return string.IsNullOrEmpty(dir) ? "" : Path.Combine(dir, "index.html");
        }
    }

    /// <summary>
    /// Inserts &lt;script src="./funnel.js"&gt; into the WebGL template HTML, as the first tag in &lt;head&gt;.
    /// </summary>
    public static string ConnectFunnelToHtml(bool installJsIfMissing)
    {
        if (installJsIfMissing)
        {
            string js = FunnelJsPath;
            if (string.IsNullOrEmpty(js) || !File.Exists(js))
                Install(overwrite: false, logAlways: false);
        }

        var patched = new System.Collections.Generic.List<string>();
        var already = new System.Collections.Generic.List<string>();
        var failed = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (string htmlPath in CandidateHtmlFiles())
        {
            if (!seen.Add(htmlPath))
                continue;
            switch (PatchIndexHtmlFile(htmlPath))
            {
                case HtmlPatchResult.Patched:
                    patched.Add(htmlPath);
                    break;
                case HtmlPatchResult.AlreadyConnected:
                    already.Add(htmlPath);
                    break;
                default:
                    failed.Add(htmlPath);
                    break;
            }
        }

        if (patched.Count == 0 && already.Count == 0 && failed.Count == 0)
        {
            return "Не найден index.html WebGL-шаблона.\n" +
                   "Player Settings → Resolution and Presentation → WebGL Template — выбери шаблон проекта\n" +
                   "(папка Assets/WebGLTemplates/...). Потом нажми кнопку ещё раз.";
        }

        var lines = new System.Text.StringBuilder();
        if (patched.Count > 0)
        {
            lines.AppendLine("В HTML добавлен тег воронки (первым в <head>):");
            foreach (string path in patched)
                lines.AppendLine(path);
            lines.AppendLine();
            lines.AppendLine("<script src=\"./funnel.js\"></script>");
        }
        if (already.Count > 0)
        {
            if (lines.Length > 0) lines.AppendLine();
            lines.AppendLine("Уже подключено:");
            foreach (string path in already)
                lines.AppendLine(path);
        }
        if (failed.Count > 0)
        {
            if (lines.Length > 0) lines.AppendLine();
            lines.AppendLine("Не удалось вписать <head> в:");
            foreach (string path in failed)
                lines.AppendLine(path);
        }

        AssetDatabase.Refresh();
        return lines.ToString().Trim();
    }

    enum HtmlPatchResult
    {
        Patched,
        AlreadyConnected,
        Failed
    }

    static System.Collections.Generic.IEnumerable<string> CandidateHtmlFiles()
    {
        string dir = ResolveTemplateDir();
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            string index = Path.Combine(dir, "index.html");
            if (File.Exists(index))
                yield return index;
            foreach (string file in Directory.GetFiles(dir, "*.html"))
                yield return file;
        }

        string templatesRoot = Path.Combine(Application.dataPath, "WebGLTemplates");
        if (!Directory.Exists(templatesRoot))
            yield break;
        foreach (string index in Directory.GetFiles(templatesRoot, "index.html", SearchOption.AllDirectories))
            yield return index;
    }

    static void ApplySavedFolderToFunnelJs()
    {
        string host = PeepsAnalyticsSettings.IsValidHost(PeepsAnalyticsSettings.ServerHost)
            ? PeepsAnalyticsSettings.ServerHost
            : null;
        string folder = PeepsAnalyticsSettings.IsValidFolder(PeepsAnalyticsSettings.GameFolder)
            ? PeepsAnalyticsSettings.GameFolder
            : null;
        if (host != null || folder != null)
            PeepsAnalyticsSync.ApplyToFunnelJs(host, folder);
    }

    public static string ResolveTemplateDir()
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

    static HtmlPatchResult PatchIndexHtmlFile(string indexPath)
    {
        if (string.IsNullOrEmpty(indexPath) || !File.Exists(indexPath))
            return HtmlPatchResult.Failed;

        string html = File.ReadAllText(indexPath);
        if (Regex.IsMatch(html, @"<script[^>]+src\s*=\s*['""][^'""]*funnel\.js['""]", RegexOptions.IgnoreCase))
            return HtmlPatchResult.AlreadyConnected;

        const string tag = "        <script src=\"./funnel.js\"></script>";
        var head = new Regex("<head[^>]*>", RegexOptions.IgnoreCase);
        if (!head.IsMatch(html))
            return HtmlPatchResult.Failed;

        html = head.Replace(html, m => m.Value + "\n" + tag, 1);
        File.WriteAllText(indexPath, html);
        Debug.Log("[BetterAnalytics] Added funnel.js script tag to " + indexPath);
        return HtmlPatchResult.Patched;
    }

    static string PackageRoot()
    {
        var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(HtmlFunnelWebGLInstaller).Assembly);
        if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
            return info.resolvedPath;
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
