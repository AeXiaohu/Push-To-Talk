using System;
using System.IO;
using System.Media;
using System.Text;

namespace PushToTalk
{
    // Owned by the form's UI thread. Disabled feedback allocates no audio resources.
    public sealed class SoundFeedback : IDisposable
    {
        private bool enabled;
        private bool disposed;
        private readonly Action<bool> playCue;
        private SoundPlayer openPlayer;
        private SoundPlayer mutePlayer;
        private MemoryStream openStream;
        private MemoryStream muteStream;

        public SoundFeedback() { }

        // Allows state-machine integration tests without initializing or playing audio.
        internal SoundFeedback(Action<bool> playCue)
        {
            if (playCue == null) throw new ArgumentNullException("playCue");
            this.playCue = playCue;
        }

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (disposed || enabled == value) return;
                if (!value)
                {
                    Release();
                    return;
                }
                try
                {
                    if (playCue != null)
                    {
                        enabled = true;
                        return;
                    }
                    openStream = new MemoryStream(CreateWave(false), false);
                    muteStream = new MemoryStream(CreateWave(true), false);
                    openPlayer = new SoundPlayer(openStream);
                    mutePlayer = new SoundPlayer(muteStream);
                    // Preload only on opt-in, so key polling never loads or generates audio.
                    openPlayer.Load();
                    mutePlayer.Load();
                    enabled = true;
                }
                catch { Release(); }
            }
        }

        public static bool ShouldPlayTransition(bool enabled, bool previousMute, bool currentMute)
        {
            return enabled && previousMute != currentMute;
        }

        public void PlayTransition(bool previousMute, bool currentMute)
        {
            if (!ShouldPlayTransition(enabled, previousMute, currentMute)) return;
            try
            {
                // Play is asynchronous. A new cue replaces the previous cue on rapid presses.
                if (playCue != null) playCue(currentMute);
                else (currentMute ? mutePlayer : openPlayer).Play();
            }
            catch { Release(); }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Release();
        }

        private void Release()
        {
            enabled = false;
            ReleasePlayer(openPlayer);
            ReleasePlayer(mutePlayer);
            openPlayer = mutePlayer = null;
            if (openStream != null) openStream.Dispose();
            if (muteStream != null) muteStream.Dispose();
            openStream = muteStream = null;
        }

        private static void ReleasePlayer(SoundPlayer player)
        {
            if (player == null) return;
            try { player.Stop(); } catch { }
            try { player.Dispose(); } catch { }
        }

        // Short, soft mono PCM cues, generated in memory; no sound assets to deploy.
        internal static byte[] CreateWave(bool muted)
        {
            const int rate = 22050;
            const int noteSamples = 882; // 40 ms per note
            const int gapSamples = 110; // 5 ms of silence
            const int fadeSamples = 154; // 7 ms edges prevent clicks
            const int sampleCount = noteSamples * 2 + gapSamples;
            using (MemoryStream wave = new MemoryStream(44 + sampleCount * 2))
            using (BinaryWriter writer = new BinaryWriter(wave, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + sampleCount * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)1); // mono
                writer.Write(rate);
                writer.Write(rate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(sampleCount * 2);
                for (int i = 0; i < sampleCount; i++)
                {
                    bool second = i >= noteSamples + gapSamples;
                    int local = second ? i - noteSamples - gapSamples : i;
                    double sample = 0;
                    if (local < noteSamples)
                    {
                        double frequency = muted ? (second ? 440 : 660) : (second ? 880 : 660);
                        double edge = Math.Min(1.0, Math.Min(local, noteSamples - 1 - local) / (double)fadeSamples);
                        sample = Math.Sin(2 * Math.PI * frequency * local / rate) * edge * 32767 * 0.10;
                    }
                    writer.Write((short)sample);
                }
                writer.Flush();
                return wave.ToArray();
            }
        }
    }
}
