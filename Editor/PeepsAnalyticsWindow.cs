using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

class PeepsAnalyticsWindow : EditorWindow
{
    string gameFolder = "";
    string status = "";
    Vector2 scroll;

    [MenuItem(HtmlFunnelWebGLInstaller.MenuRoot + "Game Folder", false, 0)]
    public static void Open()
    {
        var window = GetWindow<PeepsAnalyticsWindow>(true, "BetterAnalytics");
        window.minSize = new Vector2(440, 280);
        window.Show();
    }

    void OnEnable()
    {
        gameFolder = PeepsAnalyticsSync.GuessCurrentFolder();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("GAME_FOLDER", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Одно имя папки игры на сервере. Сохранится в префаб/сцену (AnalyticsManager, AnalyticsFunnel) и в funnel.js.",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Game Folder");
        gameFolder = EditorGUILayout.TextField(gameFolder);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        using (new EditorGUI.DisabledScope(!PeepsAnalyticsSettings.IsValid(PeepsAnalyticsSettings.Sanitize(gameFolder))))
        {
            if (GUILayout.Button("Apply to prefab + funnel.js", GUILayout.Height(28)))
                Apply();
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("HTML funnel", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(HtmlStatus(), MessageType.None);
        if (GUILayout.Button("Connect funnel.js in HTML", GUILayout.Height(28)))
        {
            status = HtmlFunnelWebGLInstaller.ConnectFunnelToHtml(installJsIfMissing: true);
            Debug.Log("[BetterAnalytics]\n" + status);
        }

        if (Event.current.type == EventType.KeyDown &&
            (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
        {
            Apply();
            Event.current.Use();
        }

        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.Space(8);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox(status, MessageType.None);
            EditorGUILayout.EndScrollView();
        }
    }

    static string HtmlStatus()
    {
        string path = HtmlFunnelWebGLInstaller.IndexHtmlPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return "index.html шаблона не найден. Выбери WebGL Template в Player Settings, затем нажми кнопку ниже.";
        string html = File.ReadAllText(path);
        if (Regex.IsMatch(html, @"<script[^>]+src\s*=\s*['""][^'""]*funnel\.js['""]", RegexOptions.IgnoreCase))
            return "Воронка уже подключена:\n" + path;
        return "Воронка ещё не в HTML. Кнопка впишет в <head>:\n<script src=\"./funnel.js\"></script>\n" + path;
    }

    void Apply()
    {
        status = PeepsAnalyticsSync.ApplyAll(gameFolder);
        gameFolder = PeepsAnalyticsSettings.GameFolder;
        Repaint();
        Debug.Log("[BetterAnalytics]\n" + status);
    }
}
