using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Optional funnel companion for AnalyticsManager.
/// Call SetFunnel(name, checkpoint) — number ("1") or text key ("Start").
/// Server uniqueness (player / session / day / none) is configured in admin Funnel tab.
/// Mode "none" records every call, including repeats in the same session.
/// Posts to …/Games/{folder}/funnel.php
/// </summary>
public class AnalyticsFunnel : MonoBehaviour
{
    public static AnalyticsFunnel Instance;

    [Header("Endpoint (used if AnalyticsManager is missing)")]
    public string serverBaseUrl = "";
    public string gameFolder = "";

    [Tooltip("Override full funnel.php URL. Leave empty to build from AnalyticsManager or fields above.")]
    public string funnelUrl = "";

    string sessionId = "";
    static bool gameReadySent;

    [Serializable]
    public class FunnelPayload
    {
        public string player_id;
        public string funnel_name;
        public string checkpoint;
        public string session_id;
        public string platformName;
        public string device;
        public string game_version;
        public float duration;
        public float gameready;
    }

    [Serializable]
    class FunnelResponse
    {
        public bool success;
        public bool inserted;
        public string uniqueness;
    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            sessionId = HtmlFunnelJs.GetSessionId();
            if (string.IsNullOrEmpty(sessionId))
                sessionId = Guid.NewGuid().ToString("N");
        }
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }
    }

    /// <summary>
    /// Records a funnel step. Checkpoint may be a number or a text key (Start, Awake, …).
    /// Repeat calls are sent; the server uniqueness setting decides whether they are stored.
    /// </summary>
    public void SetFunnel(string name, string checkpoint)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Debug.LogWarning("[AnalyticsFunnel] Empty funnel name.");
            return;
        }

        name = name.Trim();
        if (!TryNormalizeCheckpoint(checkpoint, out string cp))
        {
            Debug.LogWarning($"[AnalyticsFunnel] Bad checkpoint: '{checkpoint}'");
            return;
        }

        if (string.Equals(name, "HTML_", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(cp, "gameready", StringComparison.OrdinalIgnoreCase))
        {
            if (gameReadySent)
                return;
            gameReadySent = true;
        }

        try
        {
            StartCoroutine(SendFunnel(name, cp));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AnalyticsFunnel] Skip send: {e.Message}");
        }
    }

    /// <summary>Overload for int checkpoints.</summary>
    public void SetFunnel(string name, int checkpointNumber)
    {
        SetFunnel(name, checkpointNumber.ToString());
    }

    static bool TryNormalizeCheckpoint(string raw, out string checkpoint)
    {
        checkpoint = (raw ?? "").Trim();
        if (checkpoint.Length == 0 || checkpoint.Length > 64)
            return false;
        char first = checkpoint[0];
        if (!char.IsLetterOrDigit(first))
            return false;
        for (int i = 1; i < checkpoint.Length; i++)
        {
            char c = checkpoint[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                continue;
            return false;
        }
        return true;
    }

    string ResolveFunnelUrl()
    {
        if (!string.IsNullOrEmpty(funnelUrl))
            return funnelUrl;

        if (AnalyticsManager.Instance != null)
        {
            string baseUrl = AnalyticsManager.Instance.baseUrl;
            if (!string.IsNullOrEmpty(baseUrl) && baseUrl.EndsWith("analytics.php", StringComparison.OrdinalIgnoreCase))
                return baseUrl.Substring(0, baseUrl.Length - "analytics.php".Length) + "funnel.php";

            string server = AnalyticsManager.Instance.serverBaseUrl;
            string folder = AnalyticsManager.Instance.gameFolder;
            if (!string.IsNullOrEmpty(server) && !string.IsNullOrEmpty(folder))
                return AnalyticsManager.BuildEndpointUrl(server, folder, "funnel.php");
        }

        return AnalyticsManager.BuildEndpointUrl(serverBaseUrl, gameFolder, "funnel.php");
    }

    string ResolvePlayerId()
    {
        string htmlId = HtmlFunnelJs.GetPlayerId();
        if (!string.IsNullOrEmpty(htmlId) && htmlId.Length >= 2)
            return htmlId;

        if (AnalyticsManager.Instance != null && !string.IsNullOrEmpty(AnalyticsManager.Instance.playerId))
            return AnalyticsManager.Instance.playerId;

        string nick = PlayerPrefs.GetString("MyNick", "");
        if (nick.Length >= 2)
            return nick;

        return PlayerNickname.GetNickName();
    }

    string ResolvePlatform()
    {
        if (AnalyticsManager.Instance != null && !string.IsNullOrEmpty(AnalyticsManager.Instance._platformName))
            return AnalyticsManager.Instance._platformName;
        return Application.isEditor ? "editor" : Application.platform.ToString().ToLowerInvariant();
    }

    string ResolveDevice()
    {
        if (AnalyticsManager.Instance != null && !string.IsNullOrEmpty(AnalyticsManager.Instance._device))
            return AnalyticsManager.Instance._device;
        return Application.isMobilePlatform ? "mobile" : "pc";
    }

    string ResolveGameVersion()
    {
        if (AnalyticsManager.Instance != null && !string.IsNullOrEmpty(AnalyticsManager.Instance._gameVersion))
            return AnalyticsManager.Instance._gameVersion;
        return AnalyticsManager.ResolveGameVersion();
    }

    static float ResolveTimingElapsed()
    {
        float elapsed = HtmlFunnelJs.GetElapsedSec();
        if (elapsed <= 0f)
            elapsed = Time.realtimeSinceStartup;
        return elapsed > 0f ? elapsed : 0f;
    }

    IEnumerator SendFunnel(string funnelName, string key)
    {
        string url = ResolveFunnelUrl();
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogWarning("[AnalyticsFunnel] funnel URL is empty — skip.");
            yield break;
        }

        string playerId = ResolvePlayerId();
        if (string.IsNullOrEmpty(playerId))
        {
            // Wait briefly for Analytics/nickname init
            float wait = 0f;
            while (string.IsNullOrEmpty(playerId) && wait < 5f)
            {
                yield return null;
                wait += Time.unscaledDeltaTime;
                playerId = ResolvePlayerId();
            }
            if (string.IsNullOrEmpty(playerId))
            {
                Debug.LogWarning("[AnalyticsFunnel] No player_id — skip.");
                yield break;
            }
        }

        if (string.IsNullOrEmpty(sessionId))
            sessionId = Guid.NewGuid().ToString("N");

        FunnelPayload data = new FunnelPayload
        {
            player_id = playerId,
            funnel_name = funnelName,
            checkpoint = key,
            session_id = sessionId,
            platformName = ResolvePlatform(),
            device = ResolveDevice(),
            game_version = ResolveGameVersion(),
            duration = 0f,
            gameready = 0f
        };
        if (string.Equals(key, "gameready", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "loaded", StringComparison.OrdinalIgnoreCase))
        {
            float elapsed = ResolveTimingElapsed();
            if (elapsed > 0f)
            {
                data.duration = elapsed;
                if (string.Equals(key, "gameready", StringComparison.OrdinalIgnoreCase))
                    data.gameready = elapsed;
            }
            if (string.Equals(key, "gameready", StringComparison.OrdinalIgnoreCase))
                HtmlFunnelJs.LogGameReady(elapsed);
        }

        string json = JsonUtility.ToJson(data);
        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        yield return request.SendWebRequest();

//#if UNITY_EDITOR
        if (request.result == UnityWebRequest.Result.Success)
        {
            FunnelResponse parsed = null;
            try { parsed = JsonUtility.FromJson<FunnelResponse>(request.downloadHandler.text); }
            catch { /* ignore */ }
            string extra = parsed == null
                ? ""
                : $" inserted={parsed.inserted} mode={parsed.uniqueness}";
            Debug.Log($"[AnalyticsFunnel] {playerId} {funnelName}#{key}{extra}");
        }
        else
            Debug.LogWarning($"[AnalyticsFunnel] Error: {request.error}");
//#endif
    }
}
