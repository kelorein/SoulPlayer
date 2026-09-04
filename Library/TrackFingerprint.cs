using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SoulPlayer.Library
{
    internal static class TrackFingerprint
    {
        internal static string Compute(string filePath)
        {
            try
            {
                using (SHA256 sha256 = SHA256.Create())
                using (FileStream stream = new FileStream(
                           filePath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                {
                    return ToHex(sha256.ComputeHash(stream));
                }
            }
            catch
            {
                // Metadata and paths are not audio identity. The catalog will skip
                // persistent generated registration until a real SHA-256 fingerprint
                // can be obtained on a later scan.
                return string.Empty;
            }
        }

        internal static bool IsValidSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return false;
            }

            foreach (char character in value)
            {
                bool hex = (character >= '0' && character <= '9') ||
                           (character >= 'a' && character <= 'f') ||
                           (character >= 'A' && character <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        private static string ToHex(byte[] value)
        {
            StringBuilder builder = new StringBuilder(value.Length * 2);
            foreach (byte part in value)
            {
                builder.Append(part.ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
