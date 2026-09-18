using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class HtmlFunnelVersionBaker
{
    const string Placeholder = "__BAKED_GAME_VERSION__";

    [PostProcessBuild(100)]
    public static void OnPostProcessBuild(BuildTarget target, string targetPath)
    {
        if (target != BuildTarget.WebGL)
            return;

        string version = ReadGameVersion();
        if (string.IsNullOrEmpty(version))
            return;

        string escaped = EscapeJs(version);
        BakeFunnelJs(targetPath, escaped);
        InjectWindowVersion(targetPath, escaped);
        Debug.Log("[BetterAnalytics] Baked game version: " + version);
    }

    static string ReadGameVersion()
    {
        string version = PlayerSettings.bundleVersion;
        if (string.IsNullOrWhiteSpace(version))
            version = Application.version;
        if (string.IsNullOrWhiteSpace(version))
            return "";
        version = version.Trim();
        return version.Length > 32 ? version.Substring(0, 32) : version;
    }

    static string EscapeJs(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "").Replace("\n", "");
    }

    static void BakeFunnelJs(string targetPath, string escaped)
    {
        string funnelPath = Path.Combine(targetPath, "funnel.js");
        if (!File.Exists(funnelPath))
            return;

        string js = File.ReadAllText(funnelPath);
        if (!js.Contains(Placeholder))
            return;
        File.WriteAllText(funnelPath, js.Replace(Placeholder, escaped));
    }

    static void InjectWindowVersion(string targetPath, string escaped)
    {
        string snippet = "window.__GAME_VERSION__='" + escaped + "';";
        string[] names = { "index.html", "index.json" };
        for (int i = 0; i < names.Length; i++)
        {
            string path = Path.Combine(targetPath, names[i]);
            if (!File.Exists(path))
                continue;

            string text = File.ReadAllText(path);
            string original = text;
            if (text.IndexOf("window.__GAME_VERSION__", System.StringComparison.Ordinal) < 0)
            {
                string funnelTag = "<" + "script src=\"./funnel.js\"></" + "script>";
                string injected = "<" + "script>" + snippet + "</" + "script>\n  " + funnelTag;
                text = text.Replace(funnelTag, injected);
            }
            if (text != original)
                File.WriteAllText(path, text);
        }
    }
}
