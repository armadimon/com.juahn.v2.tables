using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Juahn.V2.Tables
{
    /// <summary>Versioned cache envelope. SHA-256 detects corruption; it is not source authentication.</summary>
    public static class TableCacheCodec
    {
        private const uint Magic = 0x3142544a; // JTB1, this package's own format.
        private const int MaxTextBytes = 4096;
        public const int DefaultMaxPayloadBytes = 64 * 1024 * 1024;
        public static byte[] Encode(TableIdentity identity, byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length > DefaultMaxPayloadBytes) throw new ArgumentException("Table payload exceeds the cache limit.", nameof(payload));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            using (var sha = SHA256.Create())
            {
                writer.Write(Magic);
                WriteString(writer, identity.Name); WriteString(writer, identity.ContentVersion); WriteString(writer, identity.SchemaHash);
                writer.Write(payload.Length); writer.Write(sha.ComputeHash(payload)); writer.Write(payload);
                return stream.ToArray();
            }
        }
        public static bool TryDecode(byte[] envelope, TableIdentity expected, out byte[] payload, int maxPayloadBytes = DefaultMaxPayloadBytes)
        {
            payload = null;
            if (envelope == null || maxPayloadBytes < 0) return false;
            try
            {
                using (var stream = new MemoryStream(envelope, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                using (var sha = SHA256.Create())
                {
                    if (reader.ReadUInt32() != Magic) return false;
                    var actual = new TableIdentity(ReadString(reader), ReadString(reader), ReadString(reader));
                    if (!actual.Equals(expected)) return false;
                    var length = reader.ReadInt32();
                    if (length < 0 || length > maxPayloadBytes || stream.Length - stream.Position != length + 32L) return false;
                    var hash = reader.ReadBytes(32);
                    var bytes = reader.ReadBytes(length);
                    var computed = sha.ComputeHash(bytes);
                    for (var i = 0; i < hash.Length; i++) if (hash[i] != computed[i]) return false;
                    payload = bytes;
                    return true;
                }
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is ArgumentException || e is OverflowException) { return false; }
        }
        private static void WriteString(BinaryWriter writer, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Invalid table identity.");
            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > MaxTextBytes) throw new ArgumentException("Table identity is too long.");
            writer.Write(bytes.Length); writer.Write(bytes);
        }
        private static string ReadString(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length < 1 || length > MaxTextBytes || reader.BaseStream.Length - reader.BaseStream.Position < length) throw new InvalidDataException();
            return new UTF8Encoding(false, true).GetString(reader.ReadBytes(length));
        }
    }
}
