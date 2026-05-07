using UnityEngine;

public static class UnitySAMWrapper
{
    public const int SampleRate = 22050;

    public static AudioClip GenerateClipFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            // UnitySAM.SAMMain() expects a *phoneme* buffer ending with byte 155.
            // Convert plain text to phonemes first via the built-in reciter.
            UnitySAM.TextToPhonemes(text, out int[] phonemeInts);
            if (phonemeInts == null || phonemeInts.Length == 0) return null;

            UnitySAM.SetInput(phonemeInts);
            Buffer buf = UnitySAM.SAMMain();
            if (buf == null) return null;

            float[] floats = buf.GetFloats();
            if (floats == null || floats.Length == 0) return null;

            var clip = AudioClip.Create("sam_clip", floats.Length, 1, SampleRate, false);
            clip.SetData(floats, 0);
            return clip;
        }
        catch (System.Exception e)
        {
            Debug.LogError("UnitySAMWrapper: failed to generate clip: " + e);
            return null;
        }
    }
}
