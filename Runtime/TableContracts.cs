using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Juahn.V2.Tables
{
    public readonly struct TableIdentity : IEquatable<TableIdentity>
    {
        public string Name { get; }
        public string ContentVersion { get; }
        public string SchemaHash { get; }
        public TableIdentity(string name, string contentVersion, string schemaHash)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(contentVersion) || string.IsNullOrWhiteSpace(schemaHash))
                throw new ArgumentException("Table name, content version and schema hash are required.");
            Name = name; ContentVersion = contentVersion; SchemaHash = schemaHash;
        }
        public bool Equals(TableIdentity other) => Name == other.Name && ContentVersion == other.ContentVersion && SchemaHash == other.SchemaHash;
        public override bool Equals(object other) => other is TableIdentity identity && Equals(identity);
        public override int GetHashCode() => (Name ?? "").GetHashCode() ^ (ContentVersion ?? "").GetHashCode() ^ (SchemaHash ?? "").GetHashCode();
    }
    public interface ITableSource
    {
        /// <summary>Fetch the requested, exact version. Transport/authentication belong to the adapter.</summary>
        Task<byte[]> ReadAsync(TableIdentity identity, CancellationToken token);
    }
    public interface ITableCache
    {
        Task<byte[]> ReadAsync(string tableName, CancellationToken token);
        /// <summary>Implementations must atomically replace existing bytes.</summary>
        Task WriteAsync(string tableName, byte[] envelope, CancellationToken token);
    }
    public interface ITableDecoder<TRow>
    {
        /// <summary>Implement CSV/JSON/binary parsing in an adapter. Must reject invalid schemas/rows.</summary>
        IReadOnlyList<TRow> Decode(byte[] payload);
    }
    public sealed class DelegateTableDecoder<TRow> : ITableDecoder<TRow>
    {
        private readonly Func<byte[], IReadOnlyList<TRow>> _decode;
        public DelegateTableDecoder(Func<byte[], IReadOnlyList<TRow>> decode) => _decode = decode ?? throw new ArgumentNullException(nameof(decode));
        public IReadOnlyList<TRow> Decode(byte[] payload) => _decode(payload);
    }
    /// <summary>Read-only indexed snapshot; applications must supply immutable row objects.</summary>
    public sealed class TableSnapshot<TKey, TRow>
    {
        private readonly Dictionary<TKey, TRow> _index;
        public TableIdentity Identity { get; }
        public IReadOnlyList<TRow> Rows { get; }
        public TableSnapshot(TableIdentity identity, IReadOnlyList<TRow> rows, Func<TRow, TKey> keySelector)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            Identity = identity;
            var copy = new TRow[rows.Count];
            _index = new Dictionary<TKey, TRow>();
            for (var i = 0; i < copy.Length; i++)
            {
                var row = rows[i];
                if (row is null) throw new ArgumentException("A table row is null.", nameof(rows));
                var key = keySelector(row);
                if (key is null || _index.ContainsKey(key)) throw new ArgumentException("Null or duplicate table key.", nameof(rows));
                copy[i] = row;
                _index.Add(key, row);
            }
            Rows = Array.AsReadOnly(copy);
        }
        public bool TryGet(TKey key, out TRow row) => _index.TryGetValue(key, out row);
    }
}
