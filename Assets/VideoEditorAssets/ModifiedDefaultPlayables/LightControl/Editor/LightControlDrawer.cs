using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

[CustomPropertyDrawer(typeof(LightControlBehaviour))]
public class LightControlDrawer : PropertyDrawer
{
    public override float GetPropertyHeight (SerializedProperty property, GUIContent label)
    {
        int fieldCount = 4;
        return fieldCount * EditorGUIUtility.singleLineHeight;
    }

    public override void OnGUI (Rect position, SerializedProperty property, GUIContent label)
    {
        // Support dark/light mode for label text
        Color originalLabelColor = GUI.color;
        if (!EditorGUIUtility.isProSkin)
        {
            GUI.color = new Color(0, 0, 0, 1); // Black for light mode
        }
        
        SerializedProperty colorProp = property.FindPropertyRelative("color");
        SerializedProperty intensityProp = property.FindPropertyRelative("intensity");
        SerializedProperty bounceIntensityProp = property.FindPropertyRelative("bounceIntensity");
        SerializedProperty rangeProp = property.FindPropertyRelative("range");

        Rect singleFieldRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.PropertyField(singleFieldRect, colorProp);
        
        singleFieldRect.y += EditorGUIUtility.singleLineHeight;
        EditorGUI.PropertyField(singleFieldRect, intensityProp);
        
        singleFieldRect.y += EditorGUIUtility.singleLineHeight;
        EditorGUI.PropertyField(singleFieldRect, bounceIntensityProp);
        
        singleFieldRect.y += EditorGUIUtility.singleLineHeight;
        EditorGUI.PropertyField(singleFieldRect, rangeProp);
        
        GUI.color = originalLabelColor;
    }
}
