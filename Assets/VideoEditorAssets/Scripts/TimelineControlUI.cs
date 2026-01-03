using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public class TimelineControlUI : MonoBehaviour
{
    [System.Serializable]
    public class EventPreset
    {
        public string presetName = "Preset 1";
        public List<UnityEvent> events = new List<UnityEvent>();
    }

    [Header("Timeline")]
    [SerializeField] private PlayableDirector director;

    [Header("Event List")]
    [SerializeField] private List<EventPreset> eventPresets = new List<EventPreset>();
    private int lastPresetCount = 0;

    [Header("UI Panels (optional)")]
    [SerializeField] private List<GameObject> panels = new List<GameObject>();

    private bool loopActive;
    private double loopStartSeconds;
    private double loopEndSeconds;

    private double Fps => GetTimelineFps();

    private void OnValidate()
    {
        // Auto-name only newly added presets
        if (eventPresets.Count > lastPresetCount)
        {
            for (int i = lastPresetCount; i < eventPresets.Count; i++)
            {
                eventPresets[i].presetName = $"Preset {i}";
            }
        }
        lastPresetCount = eventPresets.Count;
    }

#if UNITY_EDITOR
    [ContextMenu("Auto-Name All Presets")]
    private void AutoNameAllPresets()
    {
        for (int i = 0; i < eventPresets.Count; i++)
        {
            eventPresets[i].presetName = $"Preset {i}";
        }
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    private void Update()
    {
        if (!loopActive || director == null || director.state != PlayState.Playing)
            return;

        if (director.time > loopEndSeconds)
        {
            director.time = loopStartSeconds;
            director.Evaluate();
        }
    }

    public void ShowPanel(int index)
    {
        if (index < 0 || index >= panels.Count)
            return;
        HideAllPanels();
        panels[index].SetActive(true);
    }

    public void HideAllPanels()
    {
        foreach (var panel in panels)
        {
            if (panel != null)
                panel.SetActive(false);
        }
    }

    public void OnStopPointReached(int panelIndex = -1)
    {
        if (director == null)
            return;
        director.Pause();
        if (panelIndex >= 0)
            ShowPanel(panelIndex);
    }

    public void OnResumeButton()
    {
        if (director == null)
            return;
        HideAllPanels();
        director.Play();
    }

    public void OnJumpAndResumeButtonSeconds(double targetSeconds)
    {
        if (director == null)
            return;
        HideAllPanels();
        director.time = targetSeconds;
        director.Evaluate();
        director.Play();
    }

    public void OnJumpAndResumeButtonFrames(int targetFrames)
    {
        var targetSeconds = FramesToSeconds(targetFrames);
        OnJumpAndResumeButtonSeconds(targetSeconds);
    }

    public void StartLoopSeconds(double startSeconds, double endSeconds, int panelIndex = -1)
    {
        if (director == null)
            return;
        loopActive = true;
        loopStartSeconds = Mathf.Min((float)startSeconds, (float)endSeconds);
        loopEndSeconds = Mathf.Max((float)startSeconds, (float)endSeconds);
        if (panelIndex >= 0)
            ShowPanel(panelIndex);
    }

    public void StartLoopFrames(int startFrame, int endFrame, int panelIndex = -1)
    {
        var startSeconds = FramesToSeconds(startFrame);
        var endSeconds = FramesToSeconds(endFrame);
        StartLoopSeconds(startSeconds, endSeconds, panelIndex);
    }

    public void StopLoopAndJumpSeconds(double targetSeconds)
    {
        if (director == null)
            return;
        loopActive = false;
        HideAllPanels();
        director.time = targetSeconds;
        director.Evaluate();
        director.Play();
    }

    public void StopLoopAndJumpFrames(int targetFrame)
    {
        var targetSeconds = FramesToSeconds(targetFrame);
        StopLoopAndJumpSeconds(targetSeconds);
    }

    public void SetPreset(string presetName)
    {
        var preset = eventPresets.Find(p => p.presetName == presetName);
        if (preset != null)
        {
            foreach (var evt in preset.events)
            {
                evt?.Invoke();
            }
        }
        else
        {
            Debug.LogWarning($"[TimelineControlUI] Preset '{presetName}' not found.");
        }
    }

    private double FramesToSeconds(int frames)
    {
        var fps = Fps;
        if (fps <= 0.0001)
            fps = 30.0; // fallback
        return frames / fps;
    }

    private double GetTimelineFps()
    {
        if (director == null)
            return 0;

        if (director.playableAsset is TimelineAsset timelineAsset)
        {
            var frameRate = timelineAsset.editorSettings.frameRate;
            if (frameRate > 0.0001)
                return frameRate;
        }

        return 0;
    }
}
