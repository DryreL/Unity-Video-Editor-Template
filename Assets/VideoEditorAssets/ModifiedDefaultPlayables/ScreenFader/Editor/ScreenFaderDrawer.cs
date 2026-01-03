using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;

[CustomPropertyDrawer(typeof(ScreenFaderBehaviour))]
public class ScreenFaderDrawer : PropertyDrawer
{
    public override float GetPropertyHeight (SerializedProperty property, GUIContent label)
    {
        int fieldCount = 1;
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

        Rect singleFieldRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.PropertyField(singleFieldRect, colorProp);
        
        GUI.color = originalLabelColor;
    }
}
