using System;
using System.IO;

namespace SoulPlayer.Library
{
    internal sealed class MusicTrack
    {
        internal MusicTrack(string filePath, long sizeBytes)
        {
            FilePath = filePath;
            SizeBytes = sizeBytes;
            Extension = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
            Album = GetAlbum(filePath);

            string baseName = Path.GetFileNameWithoutExtension(filePath);
            string[] parts = baseName.Split(new[] { " - " }, 2, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                Artist = CleanName(parts[0]);
                Title = CleanName(parts[1]);
            }
            else
            {
                Artist = "Unknown artist";
                Title = CleanName(baseName);
            }
        }

        internal string FilePath { get; private set; }
        internal long SizeBytes { get; private set; }
        internal string Title { get; private set; }
        internal string Artist { get; private set; }
        internal string Album { get; private set; }
        internal string Extension { get; private set; }

        internal string SearchText
        {
            get { return (Title + " " + Artist + " " + Album + " " + Extension).ToLowerInvariant(); }
        }

        internal string DuplicateKey
        {
            get { return NormalizeForDuplicate(Path.GetFileNameWithoutExtension(FilePath)) + ":" + SizeBytes; }
        }

        private static string GetAlbum(string filePath)
        {
            DirectoryInfo parent = Directory.GetParent(filePath);
            return parent == null ? "Unknown album" : CleanName(parent.Name);
        }

        private static string CleanName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            string result = value.Trim();
            while (result.Length > 3 && char.IsDigit(result[0]) && (result[1] == ' ' || result[2] == ' ' || result[2] == '.'))
            {
                result = result.TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ' ', '-', '_');
                break;
            }

            return string.IsNullOrWhiteSpace(result) ? value.Trim() : result;
        }

        private static string NormalizeForDuplicate(string value)
        {
            char[] chars = value.ToLowerInvariant().ToCharArray();
            return new string(Array.FindAll(chars, char.IsLetterOrDigit));
        }
    }
}
