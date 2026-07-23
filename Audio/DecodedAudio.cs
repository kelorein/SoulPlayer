namespace SoulPlayer.Audio
{
    internal sealed class DecodedAudio
    {
        internal DecodedAudio(float[] samples, int channels, int sampleRate)
        {
            Samples = samples;
            Channels = channels;
            SampleRate = sampleRate;
        }

        internal float[] Samples { get; private set; }
        internal int Channels { get; private set; }
        internal int SampleRate { get; private set; }
    }
}
