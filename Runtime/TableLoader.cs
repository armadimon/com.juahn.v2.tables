using System;
using System.Threading;
using System.Threading.Tasks;

namespace Juahn.V2.Tables
{
    /// <summary>
    /// Cache-first exact-version loading. Decodes and validates before replacing a cache.
    /// Game row types and sim config mapping remain outside this package.
    /// </summary>
    public sealed class TableLoader<TKey, TRow>
    {
        private readonly ITableSource _source;
        private readonly ITableCache _cache;
        private readonly ITableDecoder<TRow> _decoder;
        private readonly Func<TRow, TKey> _key;
        private readonly Action<Exception> _cacheError;
        public TableLoader(ITableSource source, ITableDecoder<TRow> decoder, Func<TRow, TKey> key,
            ITableCache cache = null, Action<Exception> cacheError = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _cache = cache; _cacheError = cacheError;
        }
        public async Task<TableSnapshot<TKey, TRow>> LoadAsync(TableIdentity identity, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (_cache != null)
            {
                try
                {
                    var envelope = await _cache.ReadAsync(identity.Name, token).ConfigureAwait(false);
                    if (TableCacheCodec.TryDecode(envelope, identity, out var cached)) return Decode(identity, cached);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { _cacheError?.Invoke(error); }
            }
            var payload = await _source.ReadAsync(identity, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var table = Decode(identity, payload);
            if (_cache != null)
            {
                try { await _cache.WriteAsync(identity.Name, TableCacheCodec.Encode(identity, payload), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { _cacheError?.Invoke(error); }
            }
            return table;
        }
        private TableSnapshot<TKey, TRow> Decode(TableIdentity identity, byte[] payload)
        {
            if (payload == null) throw new InvalidOperationException("The table source returned no payload.");
            return new TableSnapshot<TKey, TRow>(identity, _decoder.Decode(payload), _key);
        }
    }
}
