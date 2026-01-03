using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;
using TMPro;

[TrackColor(0.195f, 0.602f, 0.859f)]
[TrackClipType(typeof(SubtitleClip))]
[TrackBindingType(typeof(TMP_Text))]
public class SubtitleTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        return ScriptPlayable<SubtitleMixerBehaviour>.Create(graph, inputCount);
    }
}
