using System;
using System.IO;
using UnityEngine;

[Serializable]
class PeepsAnalyticsSettingsData
{
    public string serverHost = "";
    public string gameFolder = "";
}

/// <summary>
/// Per-project host + GAME_FOLDER stored in ProjectSettings (not in the git package).
/// Host is only the domain, e.g. peepsgames.com — /Games is added at runtime.
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

    public static string ServerHost
    {
        get => Load().serverHost ?? "";
        set => SaveField(host: SanitizeHost(value), folder: null);
    }

    public static string GameFolder
    {
        get => Load().gameFolder ?? "";
        set => SaveField(host: null, folder: SanitizeFolder(value));
    }

    public static void Set(string serverHost, string gameFolder)
    {
        var data = Load();
        data.serverHost = SanitizeHost(serverHost);
        data.gameFolder = SanitizeFolder(gameFolder);
        Save(data);
    }

    public static string SanitizeFolder(string raw)
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

    public static string SanitizeHost(string raw)
    {
        string host = (raw ?? "").Trim();
        if (string.IsNullOrEmpty(host))
            return "";

        host = host.Replace('\\', '/');
        if (!host.Contains("://"))
            host = "https://" + host;

        if (Uri.TryCreate(host, UriKind.Absolute, out Uri uri) && !string.IsNullOrEmpty(uri.Host))
            return uri.Host.Trim().Trim('.');

        host = (raw ?? "").Trim();
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            host = host.Substring("http://".Length);
        else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            host = host.Substring("https://".Length);
        int slash = host.IndexOf('/');
        if (slash >= 0)
            host = host.Substring(0, slash);
        return host.Trim().Trim('.');
    }

    public static bool IsValidFolder(string folder)
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

    public static bool IsValidHost(string host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 253)
            return false;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        if (host.IndexOf('.') < 0)
            return false;
        for (int i = 0; i < host.Length; i++)
        {
            char c = host[i];
            if (char.IsLetterOrDigit(c) || c == '.' || c == '-')
                continue;
            return false;
        }
        return true;
    }

    static void SaveField(string host, string folder)
    {
        var data = Load();
        if (host != null)
            data.serverHost = host;
        if (folder != null)
            data.gameFolder = folder;
        Save(data);
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
