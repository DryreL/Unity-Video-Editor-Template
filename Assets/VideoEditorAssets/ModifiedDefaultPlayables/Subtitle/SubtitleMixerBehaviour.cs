using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;
using TMPro;

public class SubtitleMixerBehaviour : PlayableBehaviour
{
    string m_DefaultText;
    TMP_Text m_TmpBinding;
    Text m_UiTextBinding;
    bool m_FirstFrameHappened;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        m_TmpBinding = playerData as TMP_Text;
        m_UiTextBinding = playerData as Text;

        if (m_TmpBinding == null && m_UiTextBinding == null)
            return;

        if (!m_FirstFrameHappened)
        {
            m_DefaultText = GetCurrentText();
            m_FirstFrameHappened = true;
        }

        int inputCount = playable.GetInputCount();
        float greatestWeight = 0f;
        SubtitleBehaviour activeBehaviour = null;

        for (int i = 0; i < inputCount; i++)
        {
            float inputWeight = playable.GetInputWeight(i);
            if (inputWeight <= 0f)
                continue;

            var inputPlayable = (ScriptPlayable<SubtitleBehaviour>)playable.GetInput(i);
            var behaviour = inputPlayable.GetBehaviour();
            if (inputWeight > greatestWeight)
            {
                greatestWeight = inputWeight;
                activeBehaviour = behaviour;
            }
        }

        if (activeBehaviour != null)
        {
            SetCurrentText(activeBehaviour.ResolveText());
        }
        else if (m_FirstFrameHappened && info.effectivePlayState != PlayState.Playing)
        {
            SetCurrentText(m_DefaultText);
        }
    }

    public override void OnPlayableDestroy(Playable playable)
    {
        if (m_FirstFrameHappened)
        {
            SetCurrentText(m_DefaultText);
        }

        m_FirstFrameHappened = false;
    }

    string GetCurrentText()
    {
        if (m_TmpBinding != null)
            return m_TmpBinding.text;
        if (m_UiTextBinding != null)
            return m_UiTextBinding.text;
        return string.Empty;
    }

    void SetCurrentText(string value)
    {
        if (m_TmpBinding != null)
            m_TmpBinding.text = value;
        if (m_UiTextBinding != null)
            m_UiTextBinding.text = value;
    }
}
