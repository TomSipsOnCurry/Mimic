using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using System.IO;

public class ChatManager : MonoBehaviour
{
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private bool localPlayerOnly;
    [Header("Python")]
    [SerializeField] private string pythonExecutable = "python3";
    [SerializeField] private string speakScriptRelativePath = "VoiceAI/speak.py";

    private bool chatOpen;
    private PlayerMovement playerMovement;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();

        if (inputField == null)
        {
            UnityEngine.Debug.LogError("ChatManager: inputField NOT assigned in Inspector!");
        }
        else
        {
            inputField.gameObject.SetActive(false);
        }

        // Helpful log so we know what path Unity will use.
        UnityEngine.Debug.Log($"ChatManager: speak.py path = {GetSpeakScriptPath()}");
    }

    private void Update()
    {
        if (localPlayerOnly && playerMovement != null && !playerMovement.enabled)
        {
            return;
        }

        if (TogglePressed())
        {
            ToggleChat();
        }

        if (chatOpen && SubmitPressed())
        {
            if (inputField != null && !string.IsNullOrWhiteSpace(inputField.text))
            {
                string message = inputField.text.Trim();
                Speak(message);
                inputField.text = "";
            }
        }

        if (chatOpen && CancelPressed())
        {
            ToggleChat();
        }
    }

    private void ToggleChat()
    {
        chatOpen = !chatOpen;

        if (inputField != null)
        {
            inputField.gameObject.SetActive(chatOpen);
        }

        if (chatOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (inputField != null)
            {
                inputField.ActivateInputField();
                inputField.Select();
            }
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private static bool TogglePressed()
    {
        bool newInput = Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame;
        bool oldInput = Input.GetKeyDown(KeyCode.T);
        return newInput || oldInput;
    }

    private static bool SubmitPressed()
    {
        bool newInput = Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
        bool oldInput = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        return newInput || oldInput;
    }

    private static bool CancelPressed()
    {
        bool newInput = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool oldInput = Input.GetKeyDown(KeyCode.Escape);
        return newInput || oldInput;
    }

    private string GetSpeakScriptPath()
    {
        // Application.dataPath = .../<Project>/Assets
        // We want .../<Project>/VoiceAI/speak.py
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(projectRoot, speakScriptRelativePath));
    }

    private System.Diagnostics.Process StartPython(string scriptPath, string escapedText)
    {
        string[] executablesToTry = new[]
        {
            pythonExecutable,
            "python3",
            "python"
        };

        foreach (string exe in executablesToTry)
        {
            if (string.IsNullOrWhiteSpace(exe))
            {
                continue;
            }

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"\"{scriptPath}\" \"{escapedText}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var p = System.Diagnostics.Process.Start(psi);
                if (p != null)
                {
                    return p;
                }
            }
            catch
            {
                // Try next executable.
            }
        }

        UnityEngine.Debug.LogError(
            "ChatManager TTS: Could not start Python. In Inspector set 'pythonExecutable' to a full path (e.g. /usr/bin/python3 or /opt/homebrew/bin/python3)."
        );
        return null;
    }

    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        string scriptPath = GetSpeakScriptPath();
        if (!File.Exists(scriptPath))
        {
            UnityEngine.Debug.LogError($"ChatManager TTS: speak.py not found at: {scriptPath}");
            return;
        }

        try
        {
            string escaped = text.Replace("\"", "\\\"");
            var p = StartPython(scriptPath, escaped);
            if (p == null)
            {
                return;
            }

#if UNITY_EDITOR
            // In editor, block and log stderr/stdout to diagnose issues.
            string stderr = p.StandardError.ReadToEnd();
            string stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                UnityEngine.Debug.Log($"ChatManager TTS stdout: {stdout}");
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                UnityEngine.Debug.LogError($"ChatManager TTS stderr: {stderr}");
            }
#endif
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"ChatManager TTS exception: {e}");
        }
    }
}
