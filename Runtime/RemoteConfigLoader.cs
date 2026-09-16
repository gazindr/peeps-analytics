using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

public class RemoteConfigLoader : MonoBehaviour
{
    public static RemoteConfigLoader Instance { get; private set; }
    public static bool Inited { get; private set; }
    public static event Action ConfigUpdated;
    public static string LastConfigSource { get; private set; } = "defaults";
    public static IReadOnlyDictionary<string, string> AppliedFlags => appliedFlags;

    static readonly Dictionary<string, string> appliedFlags =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    const string CachePrefsPrefix = "RemoteConfigCache_v1_";

    [Header("Server endpoint")]
    [Tooltip("Host only, e.g. example.com")]
    public string serverBaseUrl = "";

    [Tooltip("Game folder on server")]
    public string gameFolder = "";

    [SerializeField] float fetchTimeoutSeconds = 8f;
    [SerializeField] bool showDebug = true;

    [Header("Flags")]
    [SerializeField] RemoteConfigFlag[] remoteDatas = Array.Empty<RemoteConfigFlag>();

    public RemoteConfigFlag[] Flags => remoteDatas;

    public string BuildConfigUrl()
    {
        return AnalyticsManager.BuildEndpointUrl(serverBaseUrl, gameFolder, "config.json");
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    IEnumerator Start()
    {
        yield return null;
        TryAdoptAnalyticsEndpoint();
        yield return FetchAndApply();
    }

    public void TryAdoptAnalyticsEndpoint()
    {
        var analytics = AnalyticsManager.Instance;
        if (analytics == null)
            return;
        if (!string.IsNullOrEmpty(analytics.serverBaseUrl))
            serverBaseUrl = analytics.serverBaseUrl;
        if (!string.IsNullOrEmpty(analytics.gameFolder))
            gameFolder = analytics.gameFolder;
    }

    public static string GetString(string key, string defaultValue = "")
    {
        if (string.IsNullOrEmpty(key))
            return defaultValue;
        return appliedFlags.TryGetValue(key, out string v) ? (v ?? defaultValue) : defaultValue;
    }

    public static bool TryGet(string key, out string value)
    {
        value = null;
        if (string.IsNullOrEmpty(key))
            return false;
        return appliedFlags.TryGetValue(key, out value);
    }

    public static int GetInt(string key, int defaultValue = 0)
    {
        if (!TryGet(key, out string raw) || string.IsNullOrWhiteSpace(raw))
            return defaultValue;
        return int.TryParse(raw.Trim(), out int n) ? n : defaultValue;
    }

    public static float GetFloat(string key, float defaultValue = 0f)
    {
        if (!TryGet(key, out string raw) || string.IsNullOrWhiteSpace(raw))
            return defaultValue;
        return float.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float n)
            ? n
            : defaultValue;
    }

    public static bool GetBool(string key, bool defaultValue = false)
    {
        if (!TryGet(key, out string raw) || string.IsNullOrWhiteSpace(raw))
            return defaultValue;
        return ParseBool(raw);
    }

    public static bool ParseBool(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        value = value.Trim();
        return value == "1"
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    IEnumerator FetchAndApply()
    {
        string url = BuildConfigUrl();
        if (string.IsNullOrEmpty(url))
        {
            FinishWithFlags(null, "defaults", persistCache: false);
            yield break;
        }

        Dictionary<string, string> remote = null;
        bool fetchedOk = false;
        if (showDebug)
            Debug.Log("[RemoteConfig] GET " + url);

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(fetchTimeoutSeconds));
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif
            string body = req.downloadHandler != null ? req.downloadHandler.text : "";
            if (ok && !string.IsNullOrEmpty(body) && TryParseFlags(body, out remote))
            {
                fetchedOk = true;
                if (showDebug)
                    Debug.Log("[RemoteConfig] OK flags=" + (remote != null ? remote.Count : 0));
            }
            else
            {
                Debug.LogWarning("[RemoteConfig] Fetch failed: " + req.error);
            }
        }

        if (fetchedOk)
        {
            FinishWithFlags(remote, "remote", persistCache: remote != null && remote.Count > 0);
            yield break;
        }

        if (TryLoadCachedFlags(out Dictionary<string, string> cached) && cached.Count > 0)
        {
            Debug.LogWarning("[RemoteConfig] Using cache");
            FinishWithFlags(cached, "cache", persistCache: false);
            yield break;
        }

        Debug.LogWarning("[RemoteConfig] Using local defaults");
        FinishWithFlags(null, "defaults", persistCache: false);
    }

    void FinishWithFlags(Dictionary<string, string> remoteOrCache, string source, bool persistCache)
    {
        if (persistCache && remoteOrCache != null && remoteOrCache.Count > 0)
            SaveCachedFlags(remoteOrCache);

        MergeFlags(remoteOrCache);
        LastConfigSource = source;
        Inited = true;

        if (showDebug)
        {
            var sb = new StringBuilder();
            sb.Append("[RemoteConfig] Applied source=").Append(source)
                .Append(" count=").Append(appliedFlags.Count);
            foreach (var kv in appliedFlags)
                sb.Append(" | ").Append(kv.Key).Append('=').Append(kv.Value);
            Debug.Log(sb.ToString());
        }

        ConfigUpdated?.Invoke();
    }

    void MergeFlags(Dictionary<string, string> remoteOrCache)
    {
        appliedFlags.Clear();

        if (remoteDatas != null)
        {
            for (int i = 0; i < remoteDatas.Length; i++)
            {
                var f = remoteDatas[i];
                if (f == null || string.IsNullOrEmpty(f.Key))
                    continue;
                f.Recieved = false;
                appliedFlags[f.Key] = f.Value ?? "";
            }
        }

        if (remoteOrCache != null)
        {
            foreach (var kv in remoteOrCache)
            {
                if (string.IsNullOrEmpty(kv.Key))
                    continue;
                appliedFlags[kv.Key] = kv.Value ?? "";
                MarkReceived(kv.Key);
            }
        }

        if (remoteDatas == null)
            return;
        for (int i = 0; i < remoteDatas.Length; i++)
        {
            var f = remoteDatas[i];
            if (f == null || string.IsNullOrEmpty(f.Key))
                continue;
            string v = appliedFlags.TryGetValue(f.Key, out string av) ? av : f.Value;
            if (showDebug)
                Debug.Log("Loaded " + f.Key + " :" + v + " usedDefault:" + !f.Recieved);
            f.OnLoad?.Invoke(v ?? "");
        }
    }

    void MarkReceived(string key)
    {
        if (remoteDatas == null)
            return;
        for (int i = 0; i < remoteDatas.Length; i++)
        {
            var f = remoteDatas[i];
            if (f == null || string.IsNullOrEmpty(f.Key))
                continue;
            if (!f.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;
            f.Recieved = true;
            break;
        }
    }

    string CachePrefsKey =>
        CachePrefsPrefix + (string.IsNullOrEmpty(gameFolder) ? "default" : gameFolder);

    void SaveCachedFlags(Dictionary<string, string> map)
    {
        try
        {
            var dto = new CachedFlagsDto { items = new CachedFlagEntry[map.Count] };
            int i = 0;
            foreach (var kv in map)
                dto.items[i++] = new CachedFlagEntry { k = kv.Key, v = kv.Value ?? "" };
            PlayerPrefs.SetString(CachePrefsKey, JsonUtility.ToJson(dto));
            PlayerPrefs.Save();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[RemoteConfig] Cache save failed: " + e.Message);
        }
    }

    bool TryLoadCachedFlags(out Dictionary<string, string> map)
    {
        map = null;
        if (!PlayerPrefs.HasKey(CachePrefsKey))
            return false;
        try
        {
            var dto = JsonUtility.FromJson<CachedFlagsDto>(PlayerPrefs.GetString(CachePrefsKey, ""));
            if (dto == null || dto.items == null || dto.items.Length == 0)
                return false;
            map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < dto.items.Length; i++)
            {
                var e = dto.items[i];
                if (e == null || string.IsNullOrEmpty(e.k))
                    continue;
                map[e.k] = e.v ?? "";
            }
            return map.Count > 0;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[RemoteConfig] Cache load failed: " + e.Message);
            return false;
        }
    }

    public static bool TryParseFlags(string json, out Dictionary<string, string> map)
    {
        map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(json))
            return false;

        int flagsKey = IndexOfJsonKey(json, "flags");
        if (flagsKey >= 0)
        {
            int brace = json.IndexOf('{', flagsKey);
            if (brace >= 0 && TryReadObjectPairs(json, brace, map))
                return true;
            map.Clear();
        }

        int root = json.IndexOf('{');
        return root >= 0 && TryReadObjectPairs(json, root, map);
    }

    static int IndexOfJsonKey(string json, string key)
    {
        string needle = "\"" + key + "\"";
        int pos = 0;
        while (pos < json.Length)
        {
            int found = json.IndexOf(needle, pos, StringComparison.Ordinal);
            if (found < 0)
                return -1;
            int colon = found + needle.Length;
            while (colon < json.Length && char.IsWhiteSpace(json[colon]))
                colon++;
            if (colon < json.Length && json[colon] == ':')
                return found;
            pos = found + needle.Length;
        }
        return -1;
    }

    static bool TryReadObjectPairs(string json, int brace, Dictionary<string, string> map)
    {
        if (brace < 0 || brace >= json.Length || json[brace] != '{')
            return false;

        int depth = 0;
        int end = -1;
        for (int i = brace; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '"')
            {
                i++;
                while (i < json.Length)
                {
                    if (json[i] == '\\') { i += 2; continue; }
                    if (json[i] == '"') break;
                    i++;
                }
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) { end = i; break; }
            }
        }
        if (end < 0)
            return false;

        string body = json.Substring(brace + 1, end - brace - 1);
        int pos = 0;
        while (pos < body.Length)
        {
            while (pos < body.Length && (char.IsWhiteSpace(body[pos]) || body[pos] == ','))
                pos++;
            if (pos >= body.Length || body[pos] != '"')
                break;
            if (!TryReadJsonString(body, ref pos, out string key))
                break;
            while (pos < body.Length && char.IsWhiteSpace(body[pos]))
                pos++;
            if (pos >= body.Length || body[pos] != ':')
                break;
            pos++;
            while (pos < body.Length && char.IsWhiteSpace(body[pos]))
                pos++;
            if (pos >= body.Length)
                break;

            if (body[pos] == '{' || body[pos] == '[')
            {
                SkipJsonValue(body, ref pos);
                continue;
            }

            string val;
            if (body[pos] == '"')
            {
                if (!TryReadJsonString(body, ref pos, out val))
                    break;
            }
            else
            {
                int start = pos;
                while (pos < body.Length && body[pos] != ',' && body[pos] != '}' && body[pos] != ']')
                    pos++;
                val = body.Substring(start, pos - start).Trim();
                if (val == "null") val = "";
                if (val.Equals("true", StringComparison.OrdinalIgnoreCase)) val = "1";
                if (val.Equals("false", StringComparison.OrdinalIgnoreCase)) val = "0";
            }

            if (!string.IsNullOrEmpty(key))
                map[key] = val ?? "";
        }

        return true;
    }

    static void SkipJsonValue(string s, ref int pos)
    {
        if (pos >= s.Length)
            return;
        char open = s[pos];
        char close = open == '{' ? '}' : ']';
        int depth = 0;
        for (; pos < s.Length; pos++)
        {
            char c = s[pos];
            if (c == '"')
            {
                pos++;
                while (pos < s.Length)
                {
                    if (s[pos] == '\\') { pos += 2; continue; }
                    if (s[pos] == '"') break;
                    pos++;
                }
                continue;
            }
            if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                {
                    pos++;
                    return;
                }
            }
        }
    }

    static bool TryReadJsonString(string s, ref int pos, out string result)
    {
        result = null;
        if (pos >= s.Length || s[pos] != '"')
            return false;
        pos++;
        var sb = new StringBuilder();
        while (pos < s.Length)
        {
            char c = s[pos++];
            if (c == '\\')
            {
                if (pos >= s.Length)
                    return false;
                char e = s[pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    default: sb.Append(e); break;
                }
                continue;
            }
            if (c == '"')
            {
                result = sb.ToString();
                return true;
            }
            sb.Append(c);
        }
        return false;
    }

    [Serializable]
    public class RemoteConfigFlag
    {
        public string Key;
        [Tooltip("Local default if remote/cache missing.")]
        public string Value;
        public string Note;
        public bool Recieved;
        public UnityEvent<string> OnLoad;

        public RemoteConfigFlag() { }

        public RemoteConfigFlag(string key, string defaultValue, string note = "")
        {
            Key = key;
            Value = defaultValue;
            Note = note;
        }
    }

    [Serializable]
    class CachedFlagEntry
    {
        public string k;
        public string v;
    }

    [Serializable]
    class CachedFlagsDto
    {
        public CachedFlagEntry[] items;
    }
}
