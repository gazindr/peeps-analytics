using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Shared nickname helper used by Analytics (and optionally by multiplayer).
/// Playgama is optional — resolved at runtime when present.
/// </summary>
public class PlayerNickname : MonoBehaviour
{
    public static string Nick = "";

    public static string GetNickName()
    {
        string htmlId = HtmlFunnelJs.GetPlayerId();
        if (!string.IsNullOrEmpty(htmlId) && htmlId.Length >= 2)
        {
            ApplyNick(htmlId);
            return Nick;
        }

        string platformName = TryGetAuthorizedPlatformName();
        if (!string.IsNullOrEmpty(platformName))
        {
            ApplyNick(platformName);
            return Nick;
        }

        if (Nick == null || Nick.Length < 2)
            Nick = PlayerPrefs.GetString("MyNick", "");

        if (Nick == null || Nick.Length < 2)
            return GenerateNickName();

        return Nick;
    }

    public static string GenerateNickName()
    {
        string htmlId = HtmlFunnelJs.GetPlayerId();
        if (!string.IsNullOrEmpty(htmlId) && htmlId.Length >= 2)
        {
            ApplyNick(htmlId);
            return Nick;
        }

        string platformName = TryGetAuthorizedPlatformName();
        if (!string.IsNullOrEmpty(platformName))
        {
            ApplyNick(platformName);
            return Nick;
        }

        if (Nick == null || Nick.Length < 2)
        {
            string newname = "";
            int newTypeCounter = 0;
            bool isVowl = true;
            for (int i = 0; i < 7; i++)
            {
                if (newTypeCounter == 2 || UnityEngine.Random.Range(0, 2) == 1)
                {
                    isVowl = !isVowl;
                    newTypeCounter = 0;
                }
                if (isVowl)
                    newname += RandVowl();
                else
                    newname += RandNotVowl();
                newTypeCounter++;
            }
            ApplyNick(newname);
            return Nick;
        }

        return Nick;
    }

    static string TryGetAuthorizedPlatformName()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            Type bridgeType = Type.GetType("Playgama.Bridge") ?? FindType("Playgama.Bridge");
            if (bridgeType == null)
                return null;

            PropertyInfo playerProp = bridgeType.GetProperty("player", BindingFlags.Public | BindingFlags.Static);
            object player = playerProp?.GetValue(null);
            if (player == null)
                return null;

            Type playerType = player.GetType();
            PropertyInfo authProp = playerType.GetProperty("isAuthorized", BindingFlags.Public | BindingFlags.Instance);
            if (authProp == null || !(bool)authProp.GetValue(player))
                return null;

            PropertyInfo nameProp = playerType.GetProperty("name", BindingFlags.Public | BindingFlags.Instance);
            string name = nameProp?.GetValue(player) as string;
            if (string.IsNullOrEmpty(name) || name.Length < 2)
                return null;

            return name.Trim();
        }
        catch
        {
            return null;
        }
#else
        return null;
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    static Type FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type type = assembly.GetType(fullName);
                if (type != null)
                    return type;
            }
            catch
            {
                // ignore unloadable assemblies
            }
        }
        return null;
    }
#endif

    static void ApplyNick(string name)
    {
        if (string.IsNullOrEmpty(name))
            return;

        Nick = name;
        PlayerPrefs.SetString("MyNick", Nick);
        PlayerPrefs.SetString("MyDisplayNick_", Nick);
    }

    static char RandVowl()
    {
        char[] vowls = { 'a', 'a', 'w', 'e', 'e', 'y', 'u', 'i', 'o', 'o' };
        return vowls[UnityEngine.Random.Range(0, vowls.Length)];
    }

    static char RandNotVowl()
    {
        char[] notVowls = { 'q', 'r', 't', 'p', 's', 'd', 'g', 'h', 'j', 'k', 'l', 'z', 'x', 'c', 'v', 'b', 'm' };
        return notVowls[UnityEngine.Random.Range(0, notVowls.Length)];
    }
}
