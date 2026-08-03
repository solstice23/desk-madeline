using System;
using System.Collections.Concurrent;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace DeskMadeline
{
    internal enum PetSound
    {
        Jump,
        WallJump,
        Dash,
        Land,
        DreamEnter,
        DreamExit,
        Death,
        Respawn
    }

    /// <summary>
    /// Lightweight, asset-free PCM sound player. Keeping SFX independent from the
    /// renderer avoids introducing an FMOD/game-install dependency for the pet.
    /// </summary>
    internal sealed class SoundEffects
    {
        const int SampleRate = 22050;
        readonly ConcurrentDictionary<int, byte[]> cache = new ConcurrentDictionary<int, byte[]>();
        readonly Func<bool> focused;

        public volatile int Mode;       // 0 off, 1 focused only, 2 always
        public volatile int Volume;     // 0..100

        public SoundEffects(Func<bool> focused, int mode, int volume)
        {
            this.focused = focused;
            Mode = Math.Max(0, Math.Min(2, mode));
            Volume = Math.Max(0, Math.Min(100, volume));
        }

        public void Play(PetSound sound)
        {
            int mode = Mode;
            int volume = Volume;
            if (mode == 0 || volume == 0 || (mode == 1 && !focused())) return;

            int volumeStep = Math.Max(0, Math.Min(10, (volume + 5) / 10));
            int key = (int)sound * 16 + volumeStep;
            byte[] wave = cache.GetOrAdd(key, _ => BuildWave(sound, volumeStep / 10f));
            try
            {
                var stream = new MemoryStream(wave, false);
                var player = new SoundPlayer(stream);
                player.Play();
                _ = Task.Delay(900).ContinueWith(__ =>
                {
                    player.Dispose();
                    stream.Dispose();
                }, TaskScheduler.Default);
            }
            catch { }
        }

        static byte[] BuildWave(PetSound sound, float volume)
        {
            float duration;
            switch (sound)
            {
                case PetSound.Dash: duration = 0.18f; break;
                case PetSound.Death: duration = 0.42f; break;
                case PetSound.Respawn: duration = 0.30f; break;
                case PetSound.DreamEnter:
                case PetSound.DreamExit: duration = 0.24f; break;
                default: duration = 0.13f; break;
            }

            int count = (int)(SampleRate * duration);
            var pcm = new short[count];
            uint noise = 0x13579BDFu + (uint)sound * 977u;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float p = t / duration;
                float envelope = (float)Math.Sin(Math.Min(1f, p * 12f) * Math.PI / 2f) *
                    (1f - p) * (1f - p);
                float hz, sample;
                noise = noise * 1664525u + 1013904223u;
                float n = ((noise >> 8) & 65535) / 32767.5f - 1f;
                switch (sound)
                {
                    case PetSound.Jump:
                        hz = 430f + 360f * p;
                        sample = Sine(hz, t) * 0.78f + Sine(hz * 1.5f, t) * 0.18f;
                        break;
                    case PetSound.WallJump:
                        hz = 520f + 420f * p;
                        sample = Sine(hz, t) * 0.68f + n * 0.18f;
                        break;
                    case PetSound.Dash:
                        hz = 300f + 900f * p;
                        sample = n * (0.65f - p * 0.35f) + Sine(hz, t) * 0.42f;
                        break;
                    case PetSound.Land:
                        hz = 115f - 45f * p;
                        sample = Sine(hz, t) * 0.72f + n * 0.28f;
                        break;
                    case PetSound.DreamEnter:
                        hz = 680f + 520f * p;
                        sample = Sine(hz, t) * 0.55f + Sine(hz * 1.505f, t) * 0.32f;
                        break;
                    case PetSound.DreamExit:
                        hz = 1200f - 440f * p;
                        sample = Sine(hz, t) * 0.55f + Sine(hz * 0.665f, t) * 0.32f;
                        break;
                    case PetSound.Death:
                        hz = 420f - 310f * p;
                        sample = Sine(hz, t) * 0.68f + n * 0.18f;
                        break;
                    default: // respawn
                        hz = 390f + 920f * p;
                        sample = Sine(hz, t) * 0.62f + Sine(hz * 2f, t) * 0.18f;
                        break;
                }
                float scaled = sample * envelope * volume * 0.42f;
                pcm[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, scaled * short.MaxValue));
            }

            using var stream = new MemoryStream(44 + pcm.Length * 2);
            using var writer = new BinaryWriter(stream);
            writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            writer.Write(36 + pcm.Length * 2);
            writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
            writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            writer.Write(pcm.Length * 2);
            foreach (short sample in pcm) writer.Write(sample);
            return stream.ToArray();
        }

        static float Sine(float hz, float time) => (float)Math.Sin(time * hz * Math.PI * 2.0);
    }
}
