using System;
using System.IO;
using UnityEngine;

[Serializable]
class PeepsAnalyticsSettingsData
{
    public string gameFolder = "";
}

/// <summary>
/// Per-project GAME_FOLDER stored in ProjectSettings (not in the git package).
/// </summary>
static class PeepsAnalyticsSettings
{
    const string FileName = "BetterAnalytics.json";
    const string LegacyFileName = "PeepsAnalytics.json";

    public static string FilePath
    {
        get
        {
            string project = Directory.GetParent(Application.dataPath)?.FullName ?? "";
            return Path.Combine(project, "ProjectSettings", FileName);
        }
    }

    public static string GameFolder
    {
        get => Load().gameFolder ?? "";
        set
        {
            var data = Load();
            data.gameFolder = Sanitize(value);
            Save(data);
        }
    }

    public static string Sanitize(string raw)
    {
        string folder = (raw ?? "").Trim().Replace('\\', '/').Trim('/');
        if (folder.IndexOf("://", StringComparison.Ordinal) >= 0)
        {
            try
            {
                var uri = new Uri(folder.Contains("://") ? folder : "https://" + folder);
                string[] parts = uri.AbsolutePath.Trim('/').Split('/');
                if (parts.Length > 0)
                    folder = parts[parts.Length - 1];
            }
            catch
            {
                // keep trimmed text
            }
        }

        int slash = folder.LastIndexOf('/');
        if (slash >= 0)
            folder = folder.Substring(slash + 1);

        return folder.Trim();
    }

    public static bool IsValid(string folder)
    {
        if (string.IsNullOrEmpty(folder) || folder.Length > 64)
            return false;
        if (folder == "YourGameFolder")
            return false;
        if (!char.IsLetterOrDigit(folder[0]))
            return false;
        for (int i = 1; i < folder.Length; i++)
        {
            char c = folder[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                continue;
            return false;
        }
        return true;
    }

    static PeepsAnalyticsSettingsData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonUtility.FromJson<PeepsAnalyticsSettingsData>(File.ReadAllText(FilePath))
                       ?? new PeepsAnalyticsSettingsData();
            }

            string project = Directory.GetParent(Application.dataPath)?.FullName ?? "";
            string legacy = Path.Combine(project, "ProjectSettings", LegacyFileName);
            if (File.Exists(legacy))
            {
                return JsonUtility.FromJson<PeepsAnalyticsSettingsData>(File.ReadAllText(legacy))
                       ?? new PeepsAnalyticsSettingsData();
            }
        }
        catch
        {
            // ignore corrupt file
        }
        return new PeepsAnalyticsSettingsData();
    }

    static void Save(PeepsAnalyticsSettingsData data)
    {
        string dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
    }
}
