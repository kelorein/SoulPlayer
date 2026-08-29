using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NAudio.Flac;

namespace SoulPlayer.Audio
{
    internal static class FlacDecoder
    {
        internal static DecodedAudio Decode(string path)
        {
            return DecodeProfiled(path).Audio;
        }

        internal static ProfiledDecodedAudio DecodeProfiled(string path)
        {
            Stopwatch timer = Stopwatch.StartNew();
            byte[] encoded = File.ReadAllBytes(path);
            timer.Stop();
            double fileReadMs = timer.Elapsed.TotalMilliseconds;

            using (MemoryStream encodedStream = new MemoryStream(encoded, false))
            using (FlacReader reader = new FlacReader(encodedStream))
            {
                int bits = reader.WaveFormat.BitsPerSample;
                int channels = reader.WaveFormat.Channels;
                int sampleRate = reader.WaveFormat.SampleRate;
                int bytesPerSample = bits / 8;
                if (bytesPerSample < 1 || (bits != 8 && bits != 16 && bits != 24 && bits != 32))
                {
                    throw new NotSupportedException("Unsupported FLAC bit depth: " + bits);
                }

                timer.Restart();
                byte[] bytes = ReadDecodedBytes(reader);
                timer.Stop();
                double decodeMs = timer.Elapsed.TotalMilliseconds;
                int sampleCount = bytes.Length / bytesPerSample;
                float[] samples = new float[sampleCount];

                // FLAC frame decoding is handled by NAudio. Converting the resulting PCM
                // into Unity floats is independent work, so divide it into a few large
                // partitions instead of processing millions of samples on one CPU core.
                int workers = Math.Min(
                    Math.Max(1, Environment.ProcessorCount),
                    Math.Max(1, sampleCount / (256 * 1024)));

                timer.Restart();
                if (workers <= 1)
                {
                    ConvertRange(bytes, samples, bits, bytesPerSample, 0, sampleCount);
                }
                else
                {
                    Parallel.For(0, workers, worker =>
                    {
                        int start = sampleCount * worker / workers;
                        int end = sampleCount * (worker + 1) / workers;
                        ConvertRange(bytes, samples, bits, bytesPerSample, start, end);
                    });
                }
                timer.Stop();

                return new ProfiledDecodedAudio(
                    new DecodedAudio(samples, channels, sampleRate),
                    fileReadMs,
                    decodeMs,
                    timer.Elapsed.TotalMilliseconds);
            }
        }

        private static byte[] ReadDecodedBytes(FlacReader reader)
        {
            long expectedLength = reader.Length;
            if (expectedLength > 0 && expectedLength <= int.MaxValue)
            {
                byte[] bytes = new byte[(int)expectedLength];
                byte[] buffer = new byte[128 * 1024];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int request = Math.Min(buffer.Length, bytes.Length - offset);
                    int read = reader.Read(buffer, 0, request);
                    if (read <= 0)
                    {
                        break;
                    }

                    Buffer.BlockCopy(buffer, 0, bytes, offset, read);
                    offset += read;
                }

                if (offset != bytes.Length)
                {
                    Array.Resize(ref bytes, offset);
                }

                return bytes;
            }

            using (MemoryStream pcm = new MemoryStream())
            {
                byte[] buffer = new byte[256 * 1024];
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    pcm.Write(buffer, 0, read);
                }

                return pcm.ToArray();
            }
        }

        private static void ConvertRange(
            byte[] bytes,
            float[] samples,
            int bits,
            int bytesPerSample,
            int startSample,
            int endSample)
        {
            int offset = startSample * bytesPerSample;
            for (int index = startSample; index < endSample; index++)
            {
                if (bits == 8)
                {
                    samples[index] = (bytes[offset] - 128f) / 128f;
                }
                else if (bits == 16)
                {
                    short value = (short)(bytes[offset] | (bytes[offset + 1] << 8));
                    samples[index] = value / 32768f;
                }
                else if (bits == 24)
                {
                    int value = bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);
                    if ((value & 0x800000) != 0)
                    {
                        value |= unchecked((int)0xFF000000);
                    }

                    samples[index] = value / 8388608f;
                }
                else
                {
                    int value = bytes[offset] |
                                (bytes[offset + 1] << 8) |
                                (bytes[offset + 2] << 16) |
                                (bytes[offset + 3] << 24);
                    samples[index] = value / 2147483648f;
                }

                offset += bytesPerSample;
            }
        }
    }
}
