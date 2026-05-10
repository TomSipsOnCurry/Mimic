using UnityEngine;
using TMPro;

public class IntroSequence : MonoBehaviour
{
    public TMP_Text dialogueText;
    public GameObject introPanel;
    public AudioSource audioSource;
    public AudioClip[] voiceLines;

    private int index = 0;

    private readonly string[] lines =
    {
        "The team has finally made it. Good.",
        "If you've forgotten — the world ended a year ago. We've been surviving in a dead billionaire's vault since then.",
        "Teams like yours are sent out to scavenge what's left to keep the colony running.",
        "This time, it's an abandoned mall we found two weeks ago. You're going in to collect supplies.",
        "But something is inside.",
        "The last team there killed each other.",
        "We don't know why.",
        "You'll be equipped with a bin for movement. Don't question it. Use W, A, S, D or the arrow keys.",
        "You also have a short-range walkie-talkie. The bins block sound, so use T to communicate.",
        "After you've collected 2 resources, return to the food court and wait for the others.",
        "Press Space to collect items when they glow.",
        "One thing to remember.",
        "Don't assume the voice you hear is who you think it is.",
        "Good luck and Godspeed."
    };

    private void Start()
    {
        introPanel.SetActive(true);
        ShowLine();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            NextLine();
        }
    }

    private void ShowLine()
    {
        dialogueText.text = lines[index];

        if (voiceLines != null && index < voiceLines.Length && voiceLines[index] != null)
        {
            audioSource.Stop();
            audioSource.clip = voiceLines[index];
            audioSource.Play();
        }
    }

    private void NextLine()
    {
        index++;

        if (index < lines.Length)
        {
            ShowLine();
        }
        else
        {
            EndIntro();
        }
    }

    private void EndIntro()
    {
        introPanel.SetActive(false);
    }
}
