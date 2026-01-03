using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
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
            return;
        }

        PlayableDirector director = TimelineEditor.inspectedDirector;
        UnityEngine.Object existingBinding = director != null ? director.GetGenericBinding(track) : null;

        GroupTrack groupTrack = EnsureGroupTrack(timeline);
        track.SetGroup(groupTrack);

        Undo.RegisterCompleteObjectUndo(track, "Import Subtitles");
        ClearExistingClips(timeline, track);

        string baseKey = Path.GetFileNameWithoutExtension(selectedPath);
        for (int i = 0; i < entries.Count; i++)
        {
            SubtitleEntry entry = entries[i];
            TimelineClip clip = track.CreateClip<SubtitleClip>();
            clip.displayName = SubtitleEditorUtility.Truncate(entry.Text, 24);
            clip.start = entry.StartTime;
            clip.duration = Math.Max(k_MinDuration, entry.Duration);

            SubtitleClip clipAsset = (SubtitleClip)clip.asset;
            clipAsset.template.text = entry.Text;
            clipAsset.template.localizationTable = SubtitleEditorUtility.DefaultLocalizationTable;
            clipAsset.template.localizationKey = SubtitleEditorUtility.BuildLocalizationKey(baseKey, i + 1);
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
    const int LineCount = 4;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return LineCount * EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * (LineCount - 1);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty textProp = property.FindPropertyRelative("text");
        SerializedProperty useLocProp = property.FindPropertyRelative("useLocalization");
        SerializedProperty tableProp = property.FindPropertyRelative("localizationTable");
        SerializedProperty keyProp = property.FindPropertyRelative("localizationKey");

        Rect row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.PropertyField(row, textProp);

        row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        EditorGUI.PropertyField(row, useLocProp, new GUIContent("Use Localization"));

        row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        EditorGUI.PropertyField(row, tableProp, new GUIContent("Table"));

        row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        EditorGUI.PropertyField(row, keyProp, new GUIContent("Entry Key"));
    }
}
