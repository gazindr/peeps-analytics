using System.Runtime.InteropServices;

/// <summary>
/// Reads player/session ids assigned by WebGL template script funnel.js
/// so Analytics / AnalyticsFunnel use the same identity as HTML funnel events.
/// </summary>
public static class HtmlFunnelJs
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern string HtmlFunnel_GetPlayerId();

    [DllImport("__Internal")]
    static extern string HtmlFunnel_GetSessionId();

    [DllImport("__Internal")]
    static extern float HtmlFunnel_GetElapsedSec();
#endif

    public static string GetPlayerId()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            string id = HtmlFunnel_GetPlayerId();
            if (!string.IsNullOrEmpty(id) && id.Trim().Length >= 2)
                return id.Trim();
        }
        catch
        {
            // Plugin missing or template without funnel.js
        }
#endif
        return "";
    }

    public static string GetSessionId()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            string id = HtmlFunnel_GetSessionId();
            if (!string.IsNullOrEmpty(id))
                return id.Trim();
        }
        catch
        {
            // Plugin missing or template without funnel.js
        }
#endif
        return "";
    }

    public static float GetElapsedSec()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            float sec = HtmlFunnel_GetElapsedSec();
            if (sec > 0f && !float.IsNaN(sec) && !float.IsInfinity(sec))
                return sec;
        }
        catch
        {
            // Plugin missing or template without funnel.js
        }
#endif
        return 0f;
    }
}
