using UnityEngine;

// Simple signal logger for Timeline SignalReceiver
public class ExampleSignalLogger : MonoBehaviour
{
    [Tooltip("Optional label to prefix logs.")]
    public string label = "Signal";

    // Hook this to SignalReceiver UnityEvent
    public void LogSignal()
    {
        Debug.LogFormat(this, "{0}: signal received", string.IsNullOrEmpty(label) ? name : label);
    }

    // Overload with payload support
    public void LogSignal(string message)
    {
        Debug.LogFormat(this, "{0}: signal received -> {1}", string.IsNullOrEmpty(label) ? name : label, message);
    }
}
