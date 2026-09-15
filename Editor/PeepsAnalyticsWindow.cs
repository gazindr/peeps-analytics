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
        var window = GetWindow<PeepsAnalyticsWindow>(true, "Peeps Analytics");
        window.minSize = new Vector2(420, 220);
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
            "Одно имя папки на peepsgames. Сохранится в префаб/сцену (AnalyticsManager, AnalyticsFunnel) и в funnel.js.",
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

    void Apply()
    {
        status = PeepsAnalyticsSync.ApplyAll(gameFolder);
        gameFolder = PeepsAnalyticsSettings.GameFolder;
        Repaint();
        Debug.Log("[Peeps Analytics]\n" + status);
    }
}
