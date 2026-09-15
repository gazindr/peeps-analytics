using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Drop-in analytics module.
/// Set <see cref="serverBaseUrl"/> + <see cref="gameFolder"/> (e.g. YourGameFolder)
/// → …/Games/YourGameFolder/analytics.php
/// </summary>
public class AnalyticsManager : MonoBehaviour
{
    public static AnalyticsManager Instance;

    [Header("Server endpoint")]
    [Tooltip("Host or Games base. Examples: peepsgames.com  |  https://peepsgames.com  |  https://peepsgames.com/Games/")]
    public string serverBaseUrl = "https://peepsgames.com/Games/";

    [Tooltip("Game folder on server, e.g. YourGameFolder → …/Games/{gameFolder}/analytics.php")]
    public string gameFolder = "YourGameFolder";

    [Tooltip("Computed full URL. Filled automatically from serverBaseUrl + gameFolder.")]
    public string baseUrl = "";

    [Tooltip("If true and CoolMultiplayerManager/ServerManager exist, use their URL + GameFolder (like before).")]
    public bool preferMultiplayerEndpoint = false;

    [Header("Runtime")]
    [Tooltip("Seconds between heartbeat posts. 0 disables periodic send (custom events still work after Init).")]
    public float sendInterval = 15f;

    [Tooltip("If true, InitAnalytics runs automatically on Start.")]
    public bool autoInitOnStart = true;

    public string playerId;
    private static float playtimeSession;
    private static float playtimePlayer;
    private float sendTimer;
    public bool countPlaytime;
    public string _device = "";
    public string _platformName = "";
    private string nullFlag = " ";
    private bool inited;
    private bool endpointConfiguredExternally;
    string nickname;
    float fpsWindowSum;
    int fpsWindowFrames;

    [Serializable]
    public class AnalyticsData
    {
        public string player_id;
        public string flag_name;
        public int playtime_session;
        public int playtime_player;
        public string platformName;
        public string device;
        public float fps;
    }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        if (autoInitOnStart && !inited)
            InitAnalytics(Mathf.RoundToInt(sendInterval));
    }

    /// <summary>
    /// Same as multiplayer: serverBase (…/Games/) + gameFolder → …/Games/{folder}/analytics.php
    /// Also accepts a bare host → {host}/Games/{folder}/analytics.php
    /// </summary>
    public void ConfigureEndpoint(string serverBase, string folder)
    {
        if (string.IsNullOrEmpty(serverBase) || string.IsNullOrEmpty(folder))
            return;

        serverBaseUrl = serverBase;
        gameFolder = folder.Trim('/');
        endpointConfiguredExternally = true;
        RebuildBaseUrl();
    }

    public void SetBaseUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return;
        baseUrl = url;
        endpointConfiguredExternally = true;
    }

    public void RebuildBaseUrl()
    {
        baseUrl = BuildAnalyticsUrl(serverBaseUrl, gameFolder);
    }

    /// <summary>
    /// host + YourGameFolder → https://host/Games/YourGameFolder/analytics.php
    /// https://host/Games/ + YourGameFolder → https://host/Games/YourGameFolder/analytics.php
    /// </summary>
    public static string BuildAnalyticsUrl(string serverBase, string folder)
    {
        return BuildEndpointUrl(serverBase, folder, "analytics.php");
    }

    /// <summary>
    /// host + YourGameFolder + funnel.php → https://host/Games/YourGameFolder/funnel.php
    /// </summary>
    public static string BuildEndpointUrl(string serverBase, string folder, string phpFile)
    {
        if (string.IsNullOrEmpty(serverBase) || string.IsNullOrEmpty(folder))
            return "";

        string file = string.IsNullOrEmpty(phpFile) ? "analytics.php" : phpFile.TrimStart('/');
        string host = serverBase.Trim();
        if (!host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            host = "https://" + host;

        host = host.TrimEnd('/');
        string game = folder.Trim().Trim('/');

        if (host.EndsWith("/Games", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("/games", StringComparison.OrdinalIgnoreCase))
            return $"{host}/{game}/{file}";

        Uri uri;
        if (Uri.TryCreate(host, UriKind.Absolute, out uri))
        {
            string path = (uri.AbsolutePath ?? "").Trim('/');
            if (string.IsNullOrEmpty(path))
                return $"{uri.Scheme}://{uri.Authority}/Games/{game}/{file}";
        }

        return $"{host}/{game}/{file}";
    }

    public void InitAnalytics(int timer)
    {
        sendInterval = timer;
        _platformName = DetectPlatformName();
        _device = DetectDevice();
        DontDestroyOnLoad(gameObject);

        bool returningPlayer = PlayerPrefs.HasKey("MyNick");
        nickname = PlayerNickname.GetNickName();
        if (string.IsNullOrEmpty(nickname) || nickname.Length < 1)
            StartCoroutine(WaitForNickname());
        else
            playerId = nickname;

        PlayerPrefs.SetString("playerID", playerId);
        if (returningPlayer)
            playtimePlayer = PlayerPrefs.GetInt("Playtime");
        else
        {
            playtimeSession = 0;
            playtimePlayer = 0;
        }
        Debug.Log("Set nickname: " + playerId);

        if (preferMultiplayerEndpoint)
            TryResolveEndpointFromScene();

        if (!endpointConfiguredExternally || string.IsNullOrEmpty(baseUrl))
            RebuildBaseUrl();

        inited = true;
        countPlaytime = sendInterval > 0;
        sendTimer = 0f;
        Debug.Log("Analytics inited: " + baseUrl);
        SendAnalytics(nullFlag);
    }

    /// <summary>
    /// Soft lookup of CoolMultiplayerManager / ServerManager if present (no compile-time dependency).
    /// </summary>
    void TryResolveEndpointFromScene()
    {
        try
        {
            string serverBase = FindSingletonStringProperty("CoolMultiplayerManager", "baseURL");
            string folder = FindSingletonStringProperty("ServerManager", "GameFolder");
            if (!string.IsNullOrEmpty(serverBase) && !string.IsNullOrEmpty(folder))
                ConfigureEndpoint(serverBase, folder);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Analytics] Optional endpoint resolve skipped: " + e.Message);
        }
    }

    static string FindSingletonStringProperty(string typeName, string memberName)
    {
        Type type = FindTypeByName(typeName);
        if (type == null)
            return null;

        PropertyInfo instanceProp = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        object instance = instanceProp?.GetValue(null);
        if (instance == null)
        {
            FieldInfo instanceField = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            instance = instanceField?.GetValue(null);
        }
        if (instance == null)
            return null;

        FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (field != null && field.FieldType == typeof(string))
            return field.GetValue(instance) as string;

        PropertyInfo prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (prop != null && prop.PropertyType == typeof(string))
            return prop.GetValue(instance) as string;

        return null;
    }

    static Type FindTypeByName(string typeName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type type = assembly.GetType(typeName);
                if (type != null)
                    return type;

                // Prefer Assembly-CSharp singletons without scanning every type every time
                if (assembly.GetName().Name == "Assembly-CSharp")
                {
                    foreach (Type candidate in assembly.GetTypes())
                    {
                        if (candidate.Name == typeName)
                            return candidate;
                    }
                }
            }
            catch
            {
                // ignore unloadable assemblies
            }
        }
        return null;
    }

    static string DetectPlatformName()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string bridgeId = TryGetPlaygamaPlatformId();
        if (!string.IsNullOrEmpty(bridgeId))
            return bridgeId;
        return "webgl";
#else
        return Application.isEditor ? "editor" : Application.platform.ToString().ToLowerInvariant();
#endif
    }

    static string DetectDevice()
    {
#if UNITY_IOS
        return "ios";
#elif UNITY_ANDROID
        return "android";
#elif UNITY_WEBGL && !UNITY_EDITOR
        // Soft reuse of PlatformDetector if the project has it
        try
        {
            Type detector = FindTypeByName("PlatformDetector");
            MethodInfo method = detector?.GetMethod("GetPlatform", BindingFlags.Public | BindingFlags.Static);
            if (method != null)
            {
                string value = method.Invoke(null, null) as string;
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
        }
        catch { /* ignore */ }

        if (Application.isMobilePlatform)
            return "mobile";
        return "pc";
#else
        if (Application.isMobilePlatform)
            return Application.platform == RuntimePlatform.IPhonePlayer ? "ios" : "android";
        return "pc";
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    static string TryGetPlaygamaPlatformId()
    {
        try
        {
            Type bridgeType = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    bridgeType = assembly.GetType("Playgama.Bridge");
                    if (bridgeType != null)
                        break;
                }
                catch { /* ignore */ }
            }
            if (bridgeType == null)
                return null;

            PropertyInfo platformProp = bridgeType.GetProperty("platform", BindingFlags.Public | BindingFlags.Static);
            object platform = platformProp?.GetValue(null);
            if (platform == null)
                return null;

            PropertyInfo idProp = platform.GetType().GetProperty("id", BindingFlags.Public | BindingFlags.Instance);
            return idProp?.GetValue(platform) as string;
        }
        catch
        {
            return null;
        }
    }
#endif

    IEnumerator WaitForNickname()
    {
        while (PlayerPrefs.GetString("MyNick").Length < 1)
        {
            yield return null;
            PlayerNickname.GenerateNickName();
        }
        playerId = PlayerPrefs.GetString("MyNick");
        yield return null;
    }

    public bool usePlaytimeByEvent;

    private void Update()
    {
        if (!countPlaytime)
            return;

        playtimeSession += Time.deltaTime;
        playtimePlayer += Time.deltaTime;
        sendTimer += Time.deltaTime;

        float dt = Time.unscaledDeltaTime;
        if (dt > 0.0001f && dt < 1.5f)
        {
            fpsWindowSum += 1f / dt;
            fpsWindowFrames++;
        }

        if (sendInterval > 0f && sendTimer >= sendInterval)
        {
            sendTimer = 0f;
            SendAnalytics(nullFlag);
        }
    }

    string cachedFlag = "";

    public string AddFlag(string flagName)
    {
        if (string.IsNullOrEmpty(cachedFlag))
            return cachedFlag = flagName;

        var flags = new System.Collections.Generic.List<string>(cachedFlag.Split('|'));
        if (!flags.Contains(flagName))
            flags.Add(flagName);
        return cachedFlag = string.Join(" | ", flags);
    }

    public void SendCustomEvent(string _event)
    {
        if (!inited)
            InitAnalytics(Mathf.RoundToInt(sendInterval));
        SendAnalytics(_event);
    }

    private void SendAnalytics(string flags)
    {
        StartCoroutine(SendCoroutine(flags));
    }

    public int GetPlaytimePlayer()
    {
        return (int)playtimePlayer;
    }

    static float TryGetDevPanelAverageFps(float windowSeconds)
    {
        try
        {
            Type panel = FindTypeByName("RuntimeDevPanel");
            MethodInfo method = panel?.GetMethod("GetAverageFps", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
                return 0f;
            object value = method.Invoke(null, new object[] { windowSeconds });
            return value is float f ? f : 0f;
        }
        catch
        {
            return 0f;
        }
    }

    float ConsumeAverageFps()
    {
        float local = fpsWindowFrames > 0 ? fpsWindowSum / fpsWindowFrames : 0f;
        fpsWindowSum = 0f;
        fpsWindowFrames = 0;

        float window = sendInterval > 0.5f ? sendInterval : 15f;
        float fromPanel = TryGetDevPanelAverageFps(window);
        float fps = local >= 1f ? local : fromPanel;
        if (fps < 1f || fps > 240f)
            return 0f;
        return Mathf.Round(fps * 10f) / 10f;
    }

    private IEnumerator SendCoroutine(string flags)
    {
        if (string.IsNullOrEmpty(baseUrl))
        {
            Debug.LogWarning("[Analytics] baseUrl is empty — skip send.");
            yield break;
        }

        AnalyticsData data = new AnalyticsData();
        data.player_id = playerId;
        data.flag_name = flags ?? "";
        data.playtime_session = Mathf.FloorToInt(playtimeSession);
        int _pt = Mathf.FloorToInt(playtimePlayer);
        data.playtime_player = _pt;
        data.device = _device;
        data.platformName = _platformName;
        data.fps = ConsumeAverageFps();
        PlayerPrefs.SetInt("Playtime", _pt);
        string json = JsonUtility.ToJson(data);

        UnityWebRequest request = new UnityWebRequest(baseUrl, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

#if UNITY_EDITOR
        if (request.result == UnityWebRequest.Result.Success)
            Debug.Log($"[Analytics] {playerId}  Sent flags: {flags}, playtime: {Mathf.FloorToInt(playtimeSession)}s, fps: {data.fps:0.0}");
        else
            Debug.LogWarning($"[Analytics] Error sending analytics: {request.error}");
#endif
    }
}
