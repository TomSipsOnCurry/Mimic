using UnityEngine;
using UnityEngine.InputSystem;
using System.Diagnostics;

public class ChatManager : MonoBehaviour
{
    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"VoiceAI/speak.py \"{text}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(psi);
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"TTS Error: {e.Message}");
        }
    }
}
