using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEditor;
using UnityEngine.Timeline;

[CustomEditor(typeof(VideoScriptPlayableAsset))]
[CanEditMultipleObjects]
public class VideoScriptPlayableAssetEditor : Editor
{
    SerializedProperty rawImage;
    SerializedProperty audioSource;

    void OnEnable()
    {
        rawImage = serializedObject.FindProperty("image");
        audioSource = serializedObject.FindProperty("audioSource");
    }

    public override void OnInspectorGUI()
    {
        VideoScriptPlayableAsset videoAsset = (VideoScriptPlayableAsset)target;
        GUIStyle lStyle = GetLabelStyle();
        
        // Ensure label color is set for dark/light mode compatibility
        if (lStyle.normal.textColor == Color.black)
        {
            lStyle.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;
        }

        GUILayout.Label("UI RawImage to play the video on:", lStyle);
        EditorGUILayout.PropertyField(rawImage);
        GUILayout.Space(10);

        // Video Source Mode Selection
        GUILayout.Label("Video Source:", lStyle);
        VideoSourceMode newMode = (VideoSourceMode)EditorGUILayout.EnumPopup("Source Mode", videoAsset.videoSourceMode);
        videoAsset.videoSourceMode = newMode;
        GUILayout.Space(10);

        // Show VideoClip field only for VideoClip mode
        if (videoAsset.videoSourceMode == VideoSourceMode.VideoClip)
        {
            VideoClip tempClip = videoAsset.videoClip;
            GUILayout.Label("Assign a Video Clip to Play:", lStyle);
            tempClip = (VideoClip)EditorGUILayout.ObjectField(tempClip, typeof(VideoClip), false);
            if (tempClip != videoAsset.videoClip && tempClip != null)
            {
                EditorUtility.DisplayDialog("New Clip Assigned", "Remember to make sure your video clip has 'Transcode' enabled in the Import Settings.", "Close");
            }
            videoAsset.videoClip = tempClip;
        }
        // Show StreamingAsset path field only for URL mode
        else if (videoAsset.videoSourceMode == VideoSourceMode.StreamingAssetURL)
        {
            GUILayout.Label("Streaming Asset URL:", lStyle);
            EditorGUILayout.HelpBox("Enter the path relative to StreamingAssets folder.\nExample: Videos/video1.mp4", MessageType.Info);
            videoAsset.streamingAssetPath = EditorGUILayout.TextField("Path (relative to StreamingAssets)", videoAsset.streamingAssetPath);
            GUILayout.Space(10);
        }

        videoAsset.loop = EditorGUILayout.Toggle("Loop Video?", videoAsset.loop);
        GUILayout.Space(10);

        videoAsset.mute = EditorGUILayout.Toggle("Mute the video?", videoAsset.mute);
        if (!videoAsset.mute)
        {
            bool useDirectAudio = videoAsset.audioOutputMode == VideoAudioOutputMode.Direct;
            useDirectAudio = EditorGUILayout.Toggle("Use Direct Audio", useDirectAudio);
            if (useDirectAudio)
            {
                videoAsset.audioOutputMode = VideoAudioOutputMode.Direct;
            } else
            {
                EditorGUILayout.PropertyField(audioSource, new GUIContent("Audio Source"));
                if (PropertyName.IsNullOrEmpty(videoAsset.audioSource.exposedName))
                {
                    videoAsset.audioOutputMode = VideoAudioOutputMode.None;
                } else
                {
                    videoAsset.audioOutputMode = VideoAudioOutputMode.AudioSource;
                }
            }
        } else
        {
            videoAsset.audioOutputMode = VideoAudioOutputMode.None;
        }
        GUILayout.Space(10);

        GUILayout.Label("Use these to clip seconds from the start and end of the video which you don't want to play:", lStyle);
        videoAsset.startTime = EditorGUILayout.DoubleField("Start Time", videoAsset.startTime);
        videoAsset.endTime = EditorGUILayout.DoubleField("End Time", videoAsset.endTime);
        GUILayout.Space(10);

        GUILayout.Label("Increase if you experience performance issues:", lStyle);
        videoAsset.preloadTime = EditorGUILayout.DoubleField("Preload Time", videoAsset.preloadTime);

        serializedObject.ApplyModifiedProperties();
    }

    private GUIStyle GetLabelStyle ()
    {
        GUIStyle labelStyle = new GUIStyle();
        labelStyle.wordWrap = true;
        // Support both light and dark modes
        labelStyle.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;
        return labelStyle;
    }
}