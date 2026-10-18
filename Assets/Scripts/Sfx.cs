using UnityEngine;

namespace MergeUp
{
    /// <summary>Sound effects synthesised at start-up (sine/saw beeps), so there are no audio files.</summary>
    public sealed class Sfx
    {
        private const int SampleRate = 44100;

        private readonly AudioSource source;
        private readonly AudioClip slide;
        private readonly AudioClip merge;
        private readonly AudioClip undo;
        private readonly AudioClip win;
        private readonly AudioClip gameOver;

        public Sfx(GameObject host)
        {
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.5f;

            slide = Tone("slide", new[] { 330f }, 0.05f, false);
            merge = Tone("merge", new[] { 523f, 784f }, 0.07f, false);
            undo = Tone("undo", new[] { 784f, 523f }, 0.06f, false);
            win = Tone("win", new[] { 523f, 659f, 784f, 1046f, 1318f }, 0.1f, false);
            gameOver = Tone("gameover", new[] { 392f, 330f, 262f, 196f }, 0.2f, true);
        }

        public void Slide() { Play(slide); }
        public void Merge() { Play(merge); }
        public void Undo() { Play(undo); }
        public void Win() { Play(win); }
        public void GameOver() { Play(gameOver); }

        private void Play(AudioClip clip)
        {
            if (clip != null) source.PlayOneShot(clip);
        }

        /// <summary>One note per frequency, each noteSeconds long, with a quick fade so notes do not click.</summary>
        private static AudioClip Tone(string name, float[] freqs, float noteSeconds, bool saw)
        {
            int perNote = Mathf.RoundToInt(noteSeconds * SampleRate);
            var data = new float[perNote * freqs.Length];
            for (int n = 0; n < freqs.Length; n++)
            {
                double phase = 0.0;
                double step = freqs[n] / SampleRate;
                for (int i = 0; i < perNote; i++)
                {
                    phase += step;
                    phase -= System.Math.Floor(phase);
                    float wave = saw ? (float)(phase * 2.0 - 1.0) : Mathf.Sin((float)(phase * 2.0 * System.Math.PI));
                    float t = (float)i / perNote;
                    float env = Mathf.Min(1f, t * 20f) * (1f - t);
                    data[n * perNote + i] = wave * env * 0.4f;
                }
            }
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
