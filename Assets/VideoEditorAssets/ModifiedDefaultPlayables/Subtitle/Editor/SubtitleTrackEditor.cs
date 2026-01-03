using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[CustomTimelineEditor(typeof(SubtitleTrack))]
public class SubtitleTrackEditor : TrackEditor
{
    static bool s_IsImporting;

    public override void OnCreate(TrackAsset track, TrackAsset copiedFrom)
    {
        base.OnCreate(track, copiedFrom);

        if (s_IsImporting)
            return;

        s_IsImporting = true;
        try
        {
            SubtitleSrtImporter.Import(track as SubtitleTrack);
        }
        finally
        {
            s_IsImporting = false;
        }
    }
}

internal static class SubtitleSrtImporter
{
    const double k_MinDuration = 0.05;

    public static void Import(SubtitleTrack track)
    {
        if (track == null || track.timelineAsset == null)
            return;

        TimelineAsset timeline = track.timelineAsset;

        string selectedPath = EditorUtility.OpenFilePanel("Import .srt subtitles", Application.dataPath, "srt");
        if (string.IsNullOrEmpty(selectedPath))
        {
            RemoveTrack(timeline, track);
            return;
        }

        string fileContents = File.ReadAllText(selectedPath);
        List<SubtitleEntry> entries = SubtitleSrtParser.Parse(fileContents);
        if (entries.Count == 0)
        {
            EditorUtility.DisplayDialog("SRT Import", "No valid subtitle entries were found in the selected file.", "OK");
            RemoveTrack(timeline, track);
            return;
        }

        var localeInfo = SubtitleLocalizationHelper.ShowLocaleSelectionDialog();
        if (localeInfo == null)
        {
            RemoveTrack(timeline, track);
            return;
        }

        PlayableDirector director = TimelineEditor.inspectedDirector;
        UnityEngine.Object existingBinding = director != null ? director.GetGenericBinding(track) : null;

        GroupTrack groupTrack = EnsureGroupTrack(timeline);
        track.SetGroup(groupTrack);

        Undo.RegisterCompleteObjectUndo(track, "Import Subtitles");
        ClearExistingClips(timeline, track);

        string baseFileName = Path.GetFileNameWithoutExtension(selectedPath);
        string tableName = "ST_" + baseFileName;
        
        SubtitleLocalizationHelper.CreateOrUpdateStringTable(tableName, localeInfo.Value.locale, localeInfo.Value.localeCode, entries);

        for (int i = 0; i < entries.Count; i++)
        {
            SubtitleEntry entry = entries[i];
            TimelineClip clip = track.CreateClip<SubtitleClip>();
            clip.displayName = SubtitleEditorUtility.Truncate(entry.Text, 24);
            clip.start = entry.StartTime;
            clip.duration = Math.Max(k_MinDuration, entry.Duration);

            SubtitleClip clipAsset = (SubtitleClip)clip.asset;
            clipAsset.template.text = entry.Text;
            clipAsset.template.useLocalization = true;
            clipAsset.template.localizationTable = tableName;
            clipAsset.template.localizationKey = SubtitleEditorUtility.BuildLocalizationKey(baseFileName, i + 1);
        }

        if (existingBinding != null && director != null)
        {
            director.SetGenericBinding(track, existingBinding);
        }

        TimelineEditor.Refresh(RefreshReason.ContentsModified);
    }

    static void RemoveTrack(TimelineAsset timeline, TrackAsset track)
    {
        if (timeline == null || track == null)
            return;

        Undo.RegisterCompleteObjectUndo(timeline, "Cancel Subtitle Track");
        timeline.DeleteTrack(track);
        TimelineEditor.Refresh(RefreshReason.ContentsModified);
    }

    static void ClearExistingClips(TimelineAsset timeline, SubtitleTrack track)
    {
        List<TimelineClip> clips = track.GetClips().ToList();
        foreach (TimelineClip clip in clips)
        {
            timeline.DeleteClip(clip);
        }
    }

    static GroupTrack EnsureGroupTrack(TimelineAsset timeline)
    {
        GroupTrack group = timeline.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(t => t.name == SubtitleEditorUtility.GroupTrackName);
        if (group != null)
            return group;

        return timeline.CreateTrack<GroupTrack>(null, SubtitleEditorUtility.GroupTrackName);
    }
}

internal static class SubtitleEditorUtility
{
    public const string GroupTrackName = "Subtitles";
    public const string DefaultLocalizationTable = "Subtitles";
    internal const string k_StringTableBasePath = "Assets/Localization/SubtitleTracks";

    public static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;

        return value.Substring(0, max - 3) + "...";
    }

    public static string BuildLocalizationKey(string baseKey, int index)
    {
        if (string.IsNullOrEmpty(baseKey))
            baseKey = "subtitle";

        StringBuilder builder = new StringBuilder(baseKey.Length + 6);
        foreach (char c in baseKey)
        {
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        }

        builder.Append("_");
        builder.Append(index.ToString("D4"));
        return builder.ToString();
    }

    public static List<string> GetStringTableNames()
    {
        List<string> result = new List<string>();

        var settingsType = Type.GetType("UnityEditor.Localization.LocalizationEditorSettings, Unity.Localization.Editor");
        if (settingsType == null)
            return result;

        var getCollections = settingsType.GetMethod("GetStringTableCollections", BindingFlags.Public | BindingFlags.Static);
        if (getCollections == null)
            return result;

        var collections = getCollections.Invoke(null, null) as System.Collections.IEnumerable;
        if (collections == null)
            return result;

        foreach (var col in collections)
        {
            string name = TryGetTableName(col);
            if (!string.IsNullOrEmpty(name) && !result.Contains(name))
                result.Add(name);
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    public static List<string> GetStringTableEntryKeys(string tableName)
    {
        var keys = new List<string>();
        if (string.IsNullOrEmpty(tableName))
            return keys;

        var settingsType = Type.GetType("UnityEditor.Localization.LocalizationEditorSettings, Unity.Localization.Editor");
        if (settingsType == null)
            return keys;

        try
        {
            var getCollection = settingsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetStringTableCollection" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));

            object collection = getCollection?.Invoke(null, new object[] { tableName });
            if (collection == null)
            {
                var getCollections = settingsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetStringTableCollections" && m.GetParameters().Length == 0);

                var collections = getCollections?.Invoke(null, null) as System.Collections.IEnumerable;
                if (collections != null)
                {
                    foreach (var col in collections)
                    {
                        if (string.Equals(TryGetTableName(col), tableName, StringComparison.OrdinalIgnoreCase))
                        {
                            collection = col;
                            break;
                        }
                    }
                }
            }

            if (collection != null)
            {
                // SharedData.Entries -> SharedTableEntry.Key
                var sharedDataProp = collection.GetType().GetProperty("SharedData", BindingFlags.Public | BindingFlags.Instance);
                var sharedData = sharedDataProp != null ? sharedDataProp.GetValue(collection, null) : null;
                var entriesProp = sharedData?.GetType().GetProperty("Entries", BindingFlags.Public | BindingFlags.Instance);
                var entries = entriesProp != null ? entriesProp.GetValue(sharedData, null) as System.Collections.IEnumerable : null;

                if (entries != null)
                {
                    foreach (var entry in entries)
                    {
                        var keyProp = entry.GetType().GetProperty("Key", BindingFlags.Public | BindingFlags.Instance);
                        var keyVal = keyProp != null ? keyProp.GetValue(entry, null) as string : null;
                        if (!string.IsNullOrEmpty(keyVal) && !keys.Contains(keyVal))
                            keys.Add(keyVal);
                    }
                }
            }

            keys.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to read entry keys for table '{tableName}': {ex.Message}");
        }

        return keys;
    }

    internal static object BuildLocaleIdentifier(string code)
    {
        if (string.IsNullOrEmpty(code))
            return null;

        var identifierType = Type.GetType("UnityEngine.Localization.LocaleIdentifier, Unity.Localization");
        if (identifierType == null)
            return null;

        try
        {
            return Activator.CreateInstance(identifierType, new object[] { code });
        }
        catch
        {
            return null;
        }
    }

    public static string GetLocalizedPreview(string tableName, string key, string fallback)
    {
        if (string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(key))
            return fallback;

        try
        {
            // Fast path: load string tables directly from assets under the table folder
            var direct = TryGetLocalizedFromAssets(tableName, key);
            if (!string.IsNullOrEmpty(direct))
                return direct;

            var settingsType = Type.GetType("UnityEditor.Localization.LocalizationEditorSettings, Unity.Localization.Editor");
            if (settingsType == null)
                return fallback;

            var getCollection = settingsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetStringTableCollection" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));

            object collection = getCollection?.Invoke(null, new object[] { tableName });
            if (collection == null)
                return fallback;

            var collectionType = collection.GetType();

            // Gather all tables in the collection (default + list)
            var tables = new List<object>();

            var getTableMethod = collectionType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetTable" && m.GetParameters().Length == 0);
            if (getTableMethod != null)
            {
                try
                {
                    var defaultTable = getTableMethod.Invoke(collection, null);
                    if (defaultTable != null)
                        tables.Add(defaultTable);
                }
                catch { /* ignore */ }
            }

            var tablesProp = collectionType.GetProperty("Tables", BindingFlags.Public | BindingFlags.Instance);
            var tablesEnum = tablesProp != null ? tablesProp.GetValue(collection, null) as System.Collections.IEnumerable : null;
            if (tablesEnum != null)
            {
                foreach (var t in tablesEnum)
                {
                    if (t != null && !tables.Contains(t))
                        tables.Add(t);
                }
            }

            foreach (var stringTable in tables)
            {
                var stringTableType = stringTable.GetType();
                var getEntryMethod = stringTableType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "GetEntry" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
                var entryValueProp = getEntryMethod?.ReturnType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);

                if (getEntryMethod != null && entryValueProp != null)
                {
                    var entry = getEntryMethod.Invoke(stringTable, new object[] { key });
                    var value = entry != null ? entryValueProp.GetValue(entry, null) as string : null;
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to get localized preview for {tableName}/{key}: {ex.Message}");
        }

        return fallback;
    }

    static string TryGetLocalizedFromAssets(string tableName, string key)
    {
        try
        {
            string folder = GetStringTableFolder(tableName);
            if (!AssetDatabase.IsValidFolder(folder))
                return null;

            var guids = AssetDatabase.FindAssets("t:StringTable", new[] { folder });
            if (guids == null || guids.Length == 0)
                return null;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var tableObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (tableObj == null)
                    continue;

                var tableType = tableObj.GetType();
                var getEntryMethod = tableType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "GetEntry" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
                var entryValueProp = getEntryMethod?.ReturnType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);

                if (getEntryMethod != null && entryValueProp != null)
                {
                    var entry = getEntryMethod.Invoke(tableObj, new object[] { key });
                    var value = entry != null ? entryValueProp.GetValue(entry, null) as string : null;
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Asset lookup failed for {tableName}/{key}: {ex.Message}");
        }

        return null;
    }

    internal static string GetStringTableFolder(string tableName)
    {
        if (string.IsNullOrEmpty(tableName))
            return k_StringTableBasePath;

        string folderPath = System.IO.Path.Combine(k_StringTableBasePath, tableName);
        return folderPath.Replace('\\', '/');
    }

    internal static void DeleteStringTableAssets(string tableName)
    {
        if (string.IsNullOrEmpty(tableName))
            return;

        try
        {
            // Search everywhere to clear both tables and shared data assets
            var guids = AssetDatabase.FindAssets(tableName);
            bool deletedAny = false;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && path.IndexOf(tableName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AssetDatabase.DeleteAsset(path);
                    deletedAny = true;
                }
            }

            // Also try direct folder delete if it matches the table name (Unity puts tables in a folder named after the collection)
            string folderPath = GetStringTableFolder(tableName);
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.DeleteAsset(folderPath);
                deletedAny = true;
            }

            if (deletedAny)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to delete string table assets for '{tableName}': {ex.Message}");
        }
    }

    static string TryGetTableName(object collection)
    {
        if (collection == null)
            return null;

        var type = collection.GetType();

        // Try direct property TableCollectionName
        var tableNameProp = type.GetProperty("TableCollectionName", BindingFlags.Public | BindingFlags.Instance);
        if (tableNameProp != null)
        {
            var val = tableNameProp.GetValue(collection, null) as string;
            if (!string.IsNullOrEmpty(val))
                return val;
        }

        // Try SharedData.TableCollectionName
        var sharedDataProp = type.GetProperty("SharedData", BindingFlags.Public | BindingFlags.Instance);
        var sharedData = sharedDataProp != null ? sharedDataProp.GetValue(collection, null) : null;
        if (sharedData != null)
        {
            var sharedNameProp = sharedData.GetType().GetProperty("TableCollectionName", BindingFlags.Public | BindingFlags.Instance);
            if (sharedNameProp != null)
            {
                var val = sharedNameProp.GetValue(sharedData, null) as string;
                if (!string.IsNullOrEmpty(val))
                    return val;
            }
        }

        // Fallback to collection's name property
        var nameProp = type.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
        if (nameProp != null)
        {
            var val = nameProp.GetValue(collection, null) as string;
            if (!string.IsNullOrEmpty(val))
                return val;
        }

        return null;
    }

    internal static bool TryDeleteStringTableCollection(Type localizationEditorSettingsType, string tableName)
    {
        if (localizationEditorSettingsType == null || string.IsNullOrEmpty(tableName))
            return false;

        bool deleted = false;

        try
        {
            // First try direct name-based removal
            var removeByName = localizationEditorSettingsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "RemoveStringTableCollection" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            if (removeByName != null)
            {
                removeByName.Invoke(null, new object[] { tableName });
                deleted = true;
            }

            // Fallback: enumerate collections and remove matches (collection + shared data)
            var getCollections = localizationEditorSettingsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetStringTableCollections" && m.GetParameters().Length == 0);
            var removeCollection = localizationEditorSettingsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "RemoveCollection" && m.GetParameters().Length == 1);

            if (getCollections != null && removeCollection != null)
            {
                var collections = getCollections.Invoke(null, null) as System.Collections.IEnumerable;
                if (collections != null)
                {
                    foreach (var col in collections)
                    {
                        if (!string.Equals(TryGetTableName(col), tableName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Remove the collection object
                        removeCollection.Invoke(null, new object[] { col });
                        deleted = true;

                        // Attempt to remove shared data if present
                        var sharedDataProp = col.GetType().GetProperty("SharedData", BindingFlags.Public | BindingFlags.Instance);
                        var sharedData = sharedDataProp != null ? sharedDataProp.GetValue(col, null) : null;
                        if (sharedData != null)
                        {
                            try { removeCollection.Invoke(null, new object[] { sharedData }); } catch { /* ignore */ }
                            DeleteObjectAsset(sharedData);
                        }

                        DeleteObjectAsset(col);
                    }
                }
            }

            // Final attempt: fetch by name and remove
            if (!deleted)
            {
                var getCollection = localizationEditorSettingsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetStringTableCollection" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
                if (getCollection != null && removeCollection != null)
                {
                    var col = getCollection.Invoke(null, new object[] { tableName });
                    if (col != null)
                    {
                        removeCollection.Invoke(null, new object[] { col });
                        DeleteObjectAsset(col);
                        deleted = true;
                    }
                }
            }

            if (deleted)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to delete StringTableCollection '{tableName}': {ex.Message}");
        }

        return deleted;
    }

    static void DeleteObjectAsset(object obj)
    {
        var unityObj = obj as UnityEngine.Object;
        if (unityObj == null)
            return;

        string path = AssetDatabase.GetAssetPath(unityObj);
        if (!string.IsNullOrEmpty(path))
        {
            AssetDatabase.DeleteAsset(path);
        }
    }
}

internal struct SubtitleEntry
{
    public double StartTime;
    public double EndTime;
    public string Text;

    public double Duration
    {
        get { return Math.Max(0, EndTime - StartTime); }
    }
}

internal static class SubtitleSrtParser
{
    static readonly Regex k_Timecode = new Regex(@"(?<start>\d{2}:\d{2}:\d{2}[,\.]\d{3})\s*-->\s*(?<end>\d{2}:\d{2}:\d{2}[,\.]\d{3})", RegexOptions.Compiled);

    public static List<SubtitleEntry> Parse(string content)
    {
        List<SubtitleEntry> entries = new List<SubtitleEntry>();
        if (string.IsNullOrWhiteSpace(content))
            return entries;

        using (StringReader reader = new StringReader(content))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (int.TryParse(line.Trim(), out _))
                {
                    line = reader.ReadLine();
                    if (line == null)
                        break;
                }

                Match match = k_Timecode.Match(line);
                if (!match.Success)
                    continue;

                double start = ParseTime(match.Groups["start"].Value);
                double end = ParseTime(match.Groups["end"].Value);

                StringBuilder textBuilder = new StringBuilder();
                while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        break;

                    if (textBuilder.Length > 0)
                        textBuilder.AppendLine();

                    textBuilder.Append(line);
                }

                if (end <= start)
                    end = start + 0.5f;

                entries.Add(new SubtitleEntry
                {
                    StartTime = start,
                    EndTime = end,
                    Text = textBuilder.ToString()
                });
            }
        }

        return entries;
    }

    static double ParseTime(string timecode)
    {
        timecode = timecode.Replace(',', '.');
        if (TimeSpan.TryParseExact(timecode, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out TimeSpan time))
            return time.TotalSeconds;

        return 0;
    }
}

[CustomPropertyDrawer(typeof(SubtitleBehaviour))]
internal class SubtitleBehaviourDrawer : PropertyDrawer
{
    const int TextLineCount = 3;
    const int TotalLineCount = 6;

    static bool IsPropertyValid(SerializedProperty property)
    {
        if (property == null)
            return false;

        try
        {
            var so = property.serializedObject;
            if (so == null)
                return false;

            var targets = so.targetObjects;
            return targets != null && targets.Length > 0 && targets[0] != null;
        }
        catch
        {
            return false;
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        try
        {
            if (!IsPropertyValid(property))
                return EditorGUIUtility.singleLineHeight;

            return (TotalLineCount + TextLineCount - 1) * EditorGUIUtility.singleLineHeight + (TotalLineCount - 1) * EditorGUIUtility.standardVerticalSpacing;
        }
        catch
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }

    [Obsolete("Unity marks PropertyDrawer.CanCacheInspectorGUI obsolete; overridden to disable caching and avoid disposed SerializedObject issues.")]
    public override bool CanCacheInspectorGUI(SerializedProperty property)
    {
        // Avoid caching to reduce chances of disposed SerializedObject bindings causing UIElements exceptions
        return false;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        try
        {
            // Guard against disposed SerializedObject during Timeline window shutdown
            if (!IsPropertyValid(property))
            {
                EditorGUI.LabelField(position, label);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

        int prevIndent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;

        SerializedProperty textProp = property.FindPropertyRelative("text");
        SerializedProperty useLocProp = property.FindPropertyRelative("useLocalization");
        SerializedProperty tableProp = property.FindPropertyRelative("localizationTable");
        SerializedProperty keyProp = property.FindPropertyRelative("localizationKey");

        Rect row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight * TextLineCount);
        EditorGUI.LabelField(new Rect(row.x, row.y, position.width, EditorGUIUtility.singleLineHeight), "Text");
        Rect textArea = new Rect(row.x, row.y + EditorGUIUtility.singleLineHeight, row.width, row.height - EditorGUIUtility.singleLineHeight);

        // Always compute preview so changing entry key/table reflects immediately
        string localizedPreview = useLocProp.boolValue
            ? SubtitleEditorUtility.GetLocalizedPreview(tableProp.stringValue, keyProp.stringValue, textProp.stringValue)
            : textProp.stringValue;

        if (useLocProp.boolValue)
        {
            // Keep serialized text in sync with chosen entry for preview
            textProp.stringValue = localizedPreview;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.TextArea(textArea, localizedPreview);
            }
        }
        else
        {
            textProp.stringValue = EditorGUI.TextArea(textArea, localizedPreview);
        }

        row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
        row.height = EditorGUIUtility.singleLineHeight;
        EditorGUI.PropertyField(row, useLocProp, new GUIContent("Use Localization"));

        row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        var tableNames = SubtitleEditorUtility.GetStringTableNames();
        if (tableNames.Count == 0)
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUI.TextField(row, "Localization Table", "<no string tables>");
            EditorGUI.EndDisabledGroup();
        }
        else
        {
            if (!string.IsNullOrEmpty(tableProp.stringValue) && !tableNames.Contains(tableProp.stringValue))
                tableNames.Add(tableProp.stringValue);

            tableNames.Sort(StringComparer.OrdinalIgnoreCase);
            int currentIndex = Mathf.Max(0, tableNames.IndexOf(tableProp.stringValue));
            int newIndex = EditorGUI.Popup(row, "Localization Table", currentIndex, tableNames.ToArray());
            if (newIndex >= 0 && newIndex < tableNames.Count)
                tableProp.stringValue = tableNames[newIndex];
        }

        row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        // Entry key dropdown driven by selected table entries
        var entryKeys = SubtitleEditorUtility.GetStringTableEntryKeys(tableProp.stringValue);
        if (!string.IsNullOrEmpty(keyProp.stringValue) && !entryKeys.Contains(keyProp.stringValue))
            entryKeys.Add(keyProp.stringValue);

        if (entryKeys.Count == 0)
            entryKeys.Add("<no entries>");

        entryKeys.Sort(StringComparer.OrdinalIgnoreCase);
        int keyIndex = Mathf.Max(0, entryKeys.IndexOf(keyProp.stringValue));
        int newKeyIndex = EditorGUI.Popup(row, "Entry Key", keyIndex, entryKeys.ToArray());
        if (newKeyIndex >= 0 && newKeyIndex < entryKeys.Count)
        {
            var selectedKey = entryKeys[newKeyIndex];
            var resolvedKey = selectedKey == "<no entries>" ? string.Empty : selectedKey;
            if (resolvedKey != keyProp.stringValue)
            {
                keyProp.stringValue = resolvedKey;

                // Update text with localized preview when localization is enabled
                if (useLocProp.boolValue)
                {
                    string preview = SubtitleEditorUtility.GetLocalizedPreview(tableProp.stringValue, resolvedKey, textProp.stringValue);
                    textProp.stringValue = preview;
                    Debug.Log($"SubtitleClip entry changed -> table: {tableProp.stringValue}, key: {resolvedKey}, preview: {preview}");
                }
            }
        }

        EditorGUI.indentLevel = prevIndent;
            EditorGUI.EndProperty();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"SubtitleBehaviourDrawer.OnGUI skipped due to disposed property: {ex.Message}");
        }
    }
}

internal static class SubtitleLocalizationHelper
{

    public struct LocaleInfo
    {
        public object locale;
        public string localeCode;
    }

    public static LocaleInfo? ShowLocaleSelectionDialog()
    {
        var localeType = Type.GetType("UnityEngine.Localization.Locale, Unity.Localization");
        if (localeType == null)
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "Localization Package Not Found",
                "Unity Localization package is not installed. Subtitles will use fallback text only.\n\nDo you want to continue without localization?",
                "Continue", "Cancel", "");

            if (choice == 0)
                return new LocaleInfo { locale = null, localeCode = "en" };
            return null;
        }

        var availableLocales = GetAllAvailableLocales(localeType);
        if (availableLocales.Count == 0)
        {
            availableLocales = GetDefaultLocaleList(localeType);
        }

        return LocaleSelectionWindow.Show(availableLocales, localeType);
    }

    static List<LocaleInfo> GetAllAvailableLocales(Type localeType)
    {
        var result = new List<LocaleInfo>();
        
        var localizationSettingsType = Type.GetType("UnityEngine.Localization.Settings.LocalizationSettings, Unity.Localization");
        if (localizationSettingsType == null)
            return result;

        var availableLocalesProp = localizationSettingsType.GetProperty("AvailableLocales", BindingFlags.Public | BindingFlags.Static);
        if (availableLocalesProp == null)
            return result;

        var availableLocales = availableLocalesProp.GetValue(null, null);
        if (availableLocales == null)
            return result;

        var localesListProp = availableLocales.GetType().GetProperty("Locales", BindingFlags.Public | BindingFlags.Instance);
        if (localesListProp == null)
            return result;

        var localesList = localesListProp.GetValue(availableLocales, null) as System.Collections.IList;
        if (localesList == null)
            return result;

        var identifierProp = localeType.GetProperty("Identifier", BindingFlags.Public | BindingFlags.Instance);
        var localeNameProp = localeType.GetProperty("LocaleName", BindingFlags.Public | BindingFlags.Instance);

        foreach (var loc in localesList)
        {
            if (loc == null) continue;

            var identifier = identifierProp?.GetValue(loc, null);
            var codeField = identifier?.GetType().GetProperty("Code", BindingFlags.Public | BindingFlags.Instance);
            string localeCode = codeField?.GetValue(identifier, null) as string;
            string localeName = localeNameProp?.GetValue(loc, null) as string;

            if (!string.IsNullOrEmpty(localeCode))
            {
                result.Add(new LocaleInfo
                {
                    locale = loc,
                    localeCode = localeCode
                });
            }
        }

        return result;
    }

    static List<LocaleInfo> GetDefaultLocaleList(Type localeType)
    {
        var result = new List<LocaleInfo>();
        var commonLocales = new[]
        {
            "en", "tr", "de", "fr", "es", "it", "pt", "ru", "ja", "ko", "zh-CN", "zh-TW", 
            "ar", "nl", "pl", "sv", "da", "fi", "no", "cs", "hu", "ro", "th", "vi", "id"
        };

        foreach (var code in commonLocales)
        {
            var locale = GetOrCreateLocale(localeType, code);
            if (locale != null)
            {
                result.Add(new LocaleInfo { locale = locale, localeCode = code });
            }
        }

        return result;
    }

    internal static object GetOrCreateLocale(Type localeType, string code)
    {
        var localizationSettingsType = Type.GetType("UnityEngine.Localization.Settings.LocalizationSettings, Unity.Localization");
        if (localizationSettingsType == null)
            return null;

        var availableLocalesProp = localizationSettingsType.GetProperty("AvailableLocales", BindingFlags.Public | BindingFlags.Static);
        if (availableLocalesProp == null)
            return null;

        var availableLocales = availableLocalesProp.GetValue(null, null);
        var localesListProp = availableLocales.GetType().GetProperty("Locales", BindingFlags.Public | BindingFlags.Instance);
        var localesList = localesListProp.GetValue(availableLocales, null) as System.Collections.IList;

        var identifierProp = localeType.GetProperty("Identifier", BindingFlags.Public | BindingFlags.Instance);
        foreach (var loc in localesList)
        {
            var identifier = identifierProp.GetValue(loc, null);
            var codeField = identifier.GetType().GetProperty("Code", BindingFlags.Public | BindingFlags.Instance);
            string localeCode = codeField.GetValue(identifier, null) as string;
            if (localeCode == code)
                return loc;
        }

        var createLocaleMethod = localeType.GetMethod("CreateLocale", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string) }, null);
        if (createLocaleMethod != null)
        {
            return createLocaleMethod.Invoke(null, new object[] { code });
        }

        return null;
    }

    public static void CreateOrUpdateStringTable(string tableName, object locale, string localeCode, List<SubtitleEntry> entries)
    {
        string tableFolder = SubtitleEditorUtility.GetStringTableFolder(tableName);
        if (!Directory.Exists(tableFolder))
        {
            Directory.CreateDirectory(tableFolder);
            AssetDatabase.Refresh();
        }

        // Always use assembly scan to avoid Type.GetType failures with different assembly names
        var stringTableCollectionType = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name.Contains("Unity.Localization"))
            .SelectMany(a => {
                try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => t.Name == "StringTableCollection");

        if (stringTableCollectionType == null)
        {
            Debug.LogError("Unity Localization package is installed but StringTableCollection type could not be loaded. String table creation skipped.");
            return;
        }

        Type localizationEditorSettingsType = Type.GetType("UnityEditor.Localization.LocalizationEditorSettings, Unity.Localization.Editor");
        if (localizationEditorSettingsType == null)
        {
            // Try to find it in editor assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                if (asm.GetName().Name.Contains("Unity.Localization.Editor"))
                {
                    var types = asm.GetTypes();
                    foreach (var t in types)
                    {
                        if (t.Name == "LocalizationEditorSettings")
                        {
                            localizationEditorSettingsType = t;
                            Debug.Log($"Found LocalizationEditorSettings in {asm.GetName().Name}");
                            break;
                        }
                    }
                    if (localizationEditorSettingsType != null) break;
                }
            }
        }

        if (localizationEditorSettingsType == null)
        {
            Debug.LogError("LocalizationEditorSettings not found. Cannot create string tables.");
            return;
        }

        var getOrCreateMethod = localizationEditorSettingsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "GetStringTableCollection" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
        
        object collection = null;
        if (getOrCreateMethod != null)
        {
            try
            {
                collection = getOrCreateMethod.Invoke(null, new object[] { tableName });
                Debug.Log($"GetStringTableCollection returned: {collection != null}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"GetStringTableCollection failed: {ex.Message}");
            }
        }

        if (collection == null)
        {
            var createCollectionMethods = localizationEditorSettingsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "CreateStringTableCollection")
                .OrderByDescending(m => m.GetParameters().Length)
                .ToArray();
            
            if (createCollectionMethods.Length > 0)
            {
                bool retriedAfterDelete = false;

                for (int attempt = 0; attempt < 2 && collection == null; attempt++)
                {
                    try
                    {
                        var createCollectionMethod = createCollectionMethods[0];
                        var parameters = createCollectionMethod.GetParameters();
                        Debug.Log($"CreateStringTableCollection has {parameters.Length} parameters");
                        
                        // Log parameter types for debugging
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            Debug.Log($"  Parameter {i}: {parameters[i].Name} ({parameters[i].ParameterType.Name})");
                        }
                        
                        object[] args = null;
                        if (parameters.Length == 3)
                        {
                            // (string tableName, string assetDirectory, IList<LocaleIdentifier> selectedLocales)
                            args = new object[] { tableName, tableFolder, null };
                        }
                        else if (parameters.Length == 2)
                        {
                            args = new object[] { tableName, tableFolder };
                        }
                        else if (parameters.Length == 1)
                        {
                            args = new object[] { tableName };
                        }

                        if (args != null)
                        {
                            collection = createCollectionMethod.Invoke(null, args);
                            Debug.Log($"CreateStringTableCollection returned: {collection != null}");
                        }
                    }
                    catch (Exception ex)
                    {
                        bool alreadyExists = ex.Message != null && ex.Message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (alreadyExists && !retriedAfterDelete)
                        {
                            Debug.Log("StringTableCollection already exists, deleting assets and recreating.");
                            SubtitleEditorUtility.DeleteStringTableAssets(tableName);
                            SubtitleEditorUtility.TryDeleteStringTableCollection(localizationEditorSettingsType, tableName);
                            AssetDatabase.Refresh();
                            retriedAfterDelete = true;
                            continue; // retry create on next loop
                        }

                        if (alreadyExists)
                        {
                            Debug.LogWarning("StringTableCollection exists and could not be deleted; attempting to fetch existing.");
                            try
                            {
                                collection = getOrCreateMethod?.Invoke(null, new object[] { tableName });
                            }
                            catch (Exception inner)
                            {
                                Debug.LogError($"Failed to retrieve existing StringTableCollection: {inner.Message}");
                            }
                        }
                        else
                        {
                            Debug.LogError($"CreateStringTableCollection failed: {ex.Message}\n{ex.InnerException?.Message}");
                        }
                    }
                }
            }
            else
            {
                Debug.LogError("CreateStringTableCollection method not found!");
            }
        }

        // Final attempt: if still null, try to fetch existing collection (in case creation failed due to duplication)
        if (collection == null && getOrCreateMethod != null)
        {
            try
            {
                collection = getOrCreateMethod.Invoke(null, new object[] { tableName });
            }
            catch (Exception ex)
            {
                Debug.LogError($"Final fetch of StringTableCollection failed: {ex.Message}");
            }
        }

        if (collection == null)
        {
            Debug.LogError("Failed to create or retrieve string table collection.");
            return;
        }

        var collectionType = collection.GetType();
        var getTableMethod = collectionType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "GetTable" && m.GetParameters().Length == 1);
        
        if (getTableMethod == null)
        {
            Debug.LogError("GetTable method not found on StringTableCollection.");
            return;
        }

        // Resolve locale identifier safely (avoid destroyed locale objects)
        if (locale is UnityEngine.Object unityObj && unityObj == null)
            locale = null;

        var localeIdentifier = locale?.GetType().GetProperty("Identifier", BindingFlags.Public | BindingFlags.Instance)?.GetValue(locale, null)
            ?? SubtitleEditorUtility.BuildLocaleIdentifier(string.IsNullOrEmpty(localeCode) ? "en" : localeCode);

        object stringTable = getTableMethod.Invoke(collection, new object[] { localeIdentifier });

        if (stringTable == null)
        {
            var addNewTableMethod = collectionType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "AddNewTable" && m.GetParameters().Length == 1);
            
            if (addNewTableMethod != null)
            {
                try
                {
                    stringTable = addNewTableMethod.Invoke(collection, new object[] { localeIdentifier });
                    Debug.Log($"AddNewTable created table: {stringTable != null}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"AddNewTable failed: {ex.Message}\n{ex.InnerException?.Message}");
                }
            }
        }

        if (stringTable == null)
        {
            Debug.LogError("Failed to get or create string table for locale: " + localeCode);
            return;
        }

        var stringTableType = stringTable.GetType();
        var addEntryMethod = stringTableType.GetMethod("AddEntry", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(string), typeof(string) }, null);
        var removeEntryMethod = stringTableType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "RemoveEntry" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
        var getEntryMethod = stringTableType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "GetEntry" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
        var entryValueProp = getEntryMethod?.ReturnType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);

        if (addEntryMethod == null)
        {
            Debug.LogError("AddEntry method not found on StringTable.");
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            string key = SubtitleEditorUtility.BuildLocalizationKey(tableName.Replace("ST_", ""), i + 1);
            string value = entries[i].Text;

            try
            {
                // Overwrite existing entries: remove then add; fallback to GetEntry().Value = value
                bool added = false;

                if (removeEntryMethod != null)
                {
                    try { removeEntryMethod.Invoke(stringTable, new object[] { key }); } catch { /* ignore */ }
                }

                try
                {
                    addEntryMethod.Invoke(stringTable, new object[] { key, value });
                    added = true;
                }
                catch
                {
                    // If AddEntry failed due to existing key, try set value via GetEntry
                    if (getEntryMethod != null && entryValueProp != null)
                    {
                        var entry = getEntryMethod.Invoke(stringTable, new object[] { key });
                        if (entry != null)
                        {
                            entryValueProp.SetValue(entry, value, null);
                            added = true;
                        }
                    }
                }

                if (!added)
                    Debug.LogWarning($"Failed to add/update entry {key}.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to add entry {key}: {ex.Message}");
            }
        }

        EditorUtility.SetDirty(stringTable as UnityEngine.Object);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"String table '{tableName}' created/updated with {entries.Count} entries for locale '{localeCode}' at {tableFolder}");
    }
}

[CustomTimelineEditor(typeof(SubtitleClip))]
internal class SubtitleClipEditor : ClipEditor
{
    public override void OnClipChanged(TimelineClip clip)
    {
        base.OnClipChanged(clip);

        var asset = clip?.asset as SubtitleClip;
        if (asset == null)
            return;

        var behaviour = asset.template;
        string preview = SubtitleEditorUtility.GetLocalizedPreview(behaviour.localizationTable, behaviour.localizationKey, behaviour.text);
        clip.displayName = SubtitleEditorUtility.Truncate(preview, 24);
    }
}

internal class LocaleSelectionWindow : EditorWindow
{
    static LocaleSelectionWindow s_Instance;
    static SubtitleLocalizationHelper.LocaleInfo? s_SelectedLocale;
    
    List<SubtitleLocalizationHelper.LocaleInfo> m_AvailableLocales;
    Type m_LocaleType;
    Vector2 m_ScrollPosition;
    string m_SearchFilter = "";
    string m_CustomCode = "";

    public static SubtitleLocalizationHelper.LocaleInfo? Show(List<SubtitleLocalizationHelper.LocaleInfo> availableLocales, Type localeType)
    {
        s_SelectedLocale = null;

        s_Instance = GetWindow<LocaleSelectionWindow>(true, "Select Subtitle Language", true);
        s_Instance.m_AvailableLocales = availableLocales;
        s_Instance.m_LocaleType = localeType;
        s_Instance.minSize = new Vector2(400, 500);
        s_Instance.maxSize = new Vector2(400, 500);
        s_Instance.ShowModal();

        return s_SelectedLocale;
    }

    void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Select the language for imported subtitles:", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        m_SearchFilter = EditorGUILayout.TextField("Search:", m_SearchFilter);
        EditorGUILayout.Space(10);

        m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition, GUILayout.Height(380));

        var filteredLocales = string.IsNullOrEmpty(m_SearchFilter)
            ? m_AvailableLocales
            : m_AvailableLocales.Where(l => l.localeCode.IndexOf(m_SearchFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

        foreach (var localeInfo in filteredLocales)
        {
            string displayName = GetLocaleDisplayName(localeInfo.localeCode);
            if (GUILayout.Button($"{displayName} ({localeInfo.localeCode})", GUILayout.Height(30)))
            {
                s_SelectedLocale = localeInfo;
                Close();
            }
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Use custom locale code (e.g., en, tr, fr, es):");
        var newCode = EditorGUILayout.TextField(m_CustomCode);
        bool submitted = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
        if (newCode != m_CustomCode)
            m_CustomCode = newCode;

        if (GUILayout.Button("Use Custom Locale", GUILayout.Height(24)) || submitted)
        {
            var code = (m_CustomCode ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(code))
            {
                var localeObj = SubtitleLocalizationHelper.GetOrCreateLocale(m_LocaleType, code);
                s_SelectedLocale = new SubtitleLocalizationHelper.LocaleInfo { locale = localeObj, localeCode = code };
                Close();
                GUIUtility.ExitGUI();
            }
        }

        EditorGUILayout.Space(10);
        if (GUILayout.Button("Cancel", GUILayout.Height(30)))
        {
            s_SelectedLocale = null;
            Close();
        }
    }

    static string GetLocaleDisplayName(string code)
    {
        var names = new Dictionary<string, string>
        {
            { "en", "English" }, { "tr", "Türkçe (Turkish)" }, { "de", "Deutsch (German)" },
            { "fr", "Français (French)" }, { "es", "Español (Spanish)" }, { "it", "Italiano (Italian)" },
            { "pt", "Português (Portuguese)" }, { "ru", "Русский (Russian)" }, { "ja", "日本語 (Japanese)" },
            { "ko", "한국어 (Korean)" }, { "zh-CN", "简体中文 (Chinese Simplified)" }, { "zh-TW", "繁體中文 (Chinese Traditional)" },
            { "ar", "العربية (Arabic)" }, { "nl", "Nederlands (Dutch)" }, { "pl", "Polski (Polish)" },
            { "sv", "Svenska (Swedish)" }, { "da", "Dansk (Danish)" }, { "fi", "Suomi (Finnish)" },
            { "no", "Norsk (Norwegian)" }, { "cs", "Čeština (Czech)" }, { "hu", "Magyar (Hungarian)" },
            { "ro", "Română (Romanian)" }, { "th", "ไทย (Thai)" }, { "vi", "Tiếng Việt (Vietnamese)" },
            { "id", "Bahasa Indonesia (Indonesian)" }, { "el", "Ελληνικά (Greek)" }, { "he", "עברית (Hebrew)" },
            { "uk", "Українська (Ukrainian)" }, { "bg", "Български (Bulgarian)" }, { "hr", "Hrvatski (Croatian)" },
            { "sk", "Slovenčina (Slovak)" }, { "sl", "Slovenščina (Slovenian)" }, { "et", "Eesti (Estonian)" },
            { "lv", "Latviešu (Latvian)" }, { "lt", "Lietuvių (Lithuanian)" }, { "fa", "فارسی (Persian)" },
            { "hi", "हिन्दी (Hindi)" }, { "bn", "বাংলা (Bengali)" }, { "ta", "தமிழ் (Tamil)" },
            { "te", "తెలుగు (Telugu)" }, { "mr", "मराठी (Marathi)" }, { "ur", "اردو (Urdu)" },
            { "ms", "Bahasa Melayu (Malay)" }, { "fil", "Filipino" }, { "sw", "Kiswahili (Swahili)" }
        };

        return names.ContainsKey(code) ? names[code] : code.ToUpper();
    }
}
