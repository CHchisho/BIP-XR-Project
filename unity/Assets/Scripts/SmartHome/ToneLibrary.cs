using UnityEngine;

namespace SmartHouse
{
    /// <summary>Generates simple procedural audio so the project needs no audio assets.</summary>
    public static class ToneLibrary
    {
        const int SampleRate = 44100;

        public static AudioClip Chime()
        {
            // Classic two-tone "ding-dong".
            return Sequence("Chime", new[] { 659.25f, 523.25f }, 0.6f, 0.9f);
        }

        public static AudioClip Click()
        {
            return Sequence("Click", new[] { 1800f }, 0.03f, 0.25f);
        }

        public static AudioClip Music()
        {
            // A calm looping arpeggio (C - Am - F - G).
            float[] notes =
            {
                261.63f, 329.63f, 392.00f, 329.63f,
                220.00f, 261.63f, 329.63f, 261.63f,
                174.61f, 220.00f, 261.63f, 220.00f,
                196.00f, 246.94f, 293.66f, 246.94f,
            };
            return Sequence("Music", notes, 0.3f, 0.35f);
        }

        static AudioClip Sequence(string name, float[] frequencies, float noteLength, float volume)
        {
            int perNote = Mathf.RoundToInt(noteLength * SampleRate);
            var data = new float[perNote * frequencies.Length];
            for (int n = 0; n < frequencies.Length; n++)
            {
                for (int i = 0; i < perNote; i++)
                {
                    float t = (float)i / SampleRate;
                    float envelope = Mathf.Min(1f, i / 400f) * Mathf.Exp(-3f * t / noteLength);
                    float wave = Mathf.Sin(2f * Mathf.PI * frequencies[n] * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * frequencies[n] * t);
                    data[n * perNote + i] = wave * envelope * volume * 0.7f;
                }
            }

            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
