using UnityEngine;
using UnityEditor;
using System.IO;
using System.Reflection;
using Type = System.Type;

public static class LayoutUtility
{

    private enum MethodType { Save, Load };

    static MethodInfo GetMethod(MethodType method_type)
    {
        // In Unity 6, WindowLayout moved to UnityEditor.WindowLayout namespace
        Type layout = Type.GetType("UnityEditor.WindowLayout,UnityEditor.CoreModule");
        
        // Fallback for older Unity versions
        if (layout == null)
        {
            layout = Type.GetType("UnityEditor.WindowLayout,UnityEditor");
        }

        MethodInfo save = null;
        MethodInfo load = null;

        if (layout != null)
        {
            load = layout.GetMethod("LoadWindowLayout", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string), typeof(bool) }, null);
            save = layout.GetMethod("SaveWindowLayout", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string) }, null);
        }

        if (method_type == MethodType.Save)
        {
            return save;
        }
        else
        {
            return load;
        }

    }

    public static void SaveLayout(string path)
    {
        path = Path.Combine(Directory.GetCurrentDirectory(), path);
        var method = GetMethod(MethodType.Save);
        if (method != null)
        {
            method.Invoke(null, new object[] { path });
        }
        else
        {
            Debug.LogWarning("SaveLayout method not found. Window layout could not be saved.");
        }
    }

    public static void LoadLayout(string path)
    {
        path = Path.Combine(Directory.GetCurrentDirectory(), path);
        var method = GetMethod(MethodType.Load);
        if (method != null)
        {
            method.Invoke(null, new object[] { path, false });
        }
        else
        {
            Debug.LogWarning("LoadLayout method not found. Window layout could not be loaded.");
        }
    }

}