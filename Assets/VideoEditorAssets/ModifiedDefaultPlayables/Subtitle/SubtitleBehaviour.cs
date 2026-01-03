using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Playables;

[Serializable]
public class SubtitleBehaviour : PlayableBehaviour
{
    [TextArea]
    public string text;
    public bool useLocalization;
    public string localizationTable = "Subtitles";
    public string localizationKey;

    public string ResolveText()
    {
        if (useLocalization && SubtitleLocalizationResolver.TryGetLocalizedString(localizationTable, localizationKey, out var localized) && !string.IsNullOrEmpty(localized))
            return localized;

        return text;
    }
}

internal static class SubtitleLocalizationResolver
{
    static readonly Type s_LocalizationSettingsType = Type.GetType("UnityEngine.Localization.Settings.LocalizationSettings, Unity.Localization");
    static readonly PropertyInfo s_StringDatabaseProp;
    static readonly MethodInfo s_GetLocalizedString;

    static SubtitleLocalizationResolver()
    {
        if (s_LocalizationSettingsType == null)
            return;

        s_StringDatabaseProp = s_LocalizationSettingsType.GetProperty("StringDatabase", BindingFlags.Public | BindingFlags.Static);
        if (s_StringDatabaseProp == null)
            return;

        var stringDbType = s_StringDatabaseProp.PropertyType;
        var methods = stringDbType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        foreach (var method in methods)
        {
            if (method.Name != "GetLocalizedString")
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length >= 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(string))
            {
                s_GetLocalizedString = method;
                break;
            }
        }
    }

    public static bool TryGetLocalizedString(string table, string key, out string value)
    {
        value = null;
        if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(key))
            return false;

        if (s_LocalizationSettingsType == null || s_StringDatabaseProp == null || s_GetLocalizedString == null)
            return false;

        var stringDb = s_StringDatabaseProp.GetValue(null, null);
        if (stringDb == null)
            return false;

        var parameters = s_GetLocalizedString.GetParameters();
        var args = new object[parameters.Length];
        for (int i = 0; i < args.Length; i++)
        {
            if (i == 0)
                args[i] = table;
            else if (i == 1)
                args[i] = key;
            else if (parameters[i].ParameterType.IsArray)
                args[i] = Array.CreateInstance(parameters[i].ParameterType.GetElementType(), 0);
            else if (parameters[i].DefaultValue != DBNull.Value)
                args[i] = parameters[i].DefaultValue;
            else
                args[i] = null;
        }

        try
        {
            var result = s_GetLocalizedString.Invoke(stringDb, args);
            value = result as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }
}
