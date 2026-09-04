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

    internal sealed class ProfiledDecodedAudio
    {
        internal ProfiledDecodedAudio(DecodedAudio audio, double fileReadMs,
            double decodeMs, double pcmConversionMs)
        {
            Audio = audio;
            FileReadMs = fileReadMs;
            DecodeMs = decodeMs;
            PcmConversionMs = pcmConversionMs;
        }

        internal DecodedAudio Audio { get; private set; }
        internal double FileReadMs { get; private set; }
        internal double DecodeMs { get; private set; }
        internal double PcmConversionMs { get; private set; }
    }
}
