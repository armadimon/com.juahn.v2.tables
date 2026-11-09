using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Juahn.V2.Tables;
using NUnit.Framework;

public class TableTests
{
    private static readonly TableIdentity Id=new TableIdentity("units","v1","schema1");
    private sealed class Storage : ITableCache,ITableSource
    {
        public byte[] Cache; public byte[] Payload=Encoding.UTF8.GetBytes("1,2,3"); public int Reads;public int Writes;public bool ThrowWrite;
        public Task<byte[]> ReadAsync(string name,CancellationToken ct)=>Task.FromResult(Cache);
        public Task<byte[]> ReadAsync(TableIdentity id,CancellationToken ct){Reads++;return Task.FromResult(Payload);}
        public Task WriteAsync(string name,byte[] bytes,CancellationToken ct){Writes++;if(ThrowWrite)throw new Exception("disk");Cache=bytes;return Task.CompletedTask;}
    }
    private static TableLoader<int,int> Loader(Storage s,Action<Exception> error=null)=>new TableLoader<int,int>(s,
        new DelegateTableDecoder<int>(b=>Encoding.UTF8.GetString(b).Split(',').Select(int.Parse).ToArray()),x=>x,s,error);
    [Test] public void CacheRoundtripChecksExactIdentityAndBytes()
    {
        var original=new byte[]{1,2,3};var b=TableCacheCodec.Encode(Id,original);
        Assert.That(TableCacheCodec.TryDecode(b,Id,out var decoded),Is.True);Assert.That(decoded,Is.EqualTo(original));
    }
    [Test] public void ContentAndSchemaChangesInvalidateCache()
    {
        var b=TableCacheCodec.Encode(Id,new byte[]{1});
        Assert.That(TableCacheCodec.TryDecode(b,new TableIdentity("units","v2","schema1"),out _),Is.False);
        Assert.That(TableCacheCodec.TryDecode(b,new TableIdentity("units","v1","schema2"),out _),Is.False);
        Assert.That(TableCacheCodec.TryDecode(b,new TableIdentity("other","v1","schema1"),out _),Is.False);
    }
    [Test] public void CorruptPayloadIsRejected()
    {var b=TableCacheCodec.Encode(Id,new byte[]{1,2,3});b[b.Length-1]^=1;Assert.That(TableCacheCodec.TryDecode(b,Id,out _),Is.False);}
    [Test] public void TruncatedAndTrailingBytesAreRejected()
    {
        var b=TableCacheCodec.Encode(Id,new byte[]{1,2,3});
        for(var i=0;i<b.Length;i++)Assert.That(TableCacheCodec.TryDecode(b.Take(i).ToArray(),Id,out _),Is.False);
        Assert.That(TableCacheCodec.TryDecode(b.Concat(new byte[]{0}).ToArray(),Id,out _),Is.False);
    }
    [Test] public void PayloadLimitIsEnforcedBeforeAllocation()
    {var b=TableCacheCodec.Encode(Id,new byte[]{1,2,3});Assert.That(TableCacheCodec.TryDecode(b,Id,out _,2),Is.False);}
    [Test] public void InvalidHeaderLengthsDoNotAllocateUnboundedBuffers()
    {
        var b=TableCacheCodec.Encode(Id,new byte[]{1});Array.Copy(BitConverter.GetBytes(int.MaxValue),0,b,4,4);
        Assert.That(TableCacheCodec.TryDecode(b,Id,out _),Is.False);
    }
    [Test] public async Task ValidCacheAvoidsSourceFetch()
    {var s=new Storage{Cache=TableCacheCodec.Encode(Id,Encoding.UTF8.GetBytes("7,8"))};var table=await Loader(s).LoadAsync(Id);Assert.That(s.Reads,Is.Zero);Assert.That(table.Rows,Is.EqualTo(new[]{7,8}));}
    [Test] public async Task InvalidCacheFallsBackAndWritesValidatedPayload()
    {var s=new Storage{Cache=new byte[]{1}};var table=await Loader(s).LoadAsync(Id);Assert.That(s.Reads,Is.EqualTo(1));Assert.That(s.Writes,Is.EqualTo(1));Assert.That(table.TryGet(2,out var row),Is.True);Assert.That(row,Is.EqualTo(2));}
    [Test] public async Task DuplicateKeysAreRejectedBeforeReplacingCache()
    {
        var original=new byte[]{1};var s=new Storage{Cache=original,Payload=Encoding.UTF8.GetBytes("1,1")};
        Assert.ThrowsAsync<ArgumentException>(async()=>await Loader(s).LoadAsync(Id));Assert.That(s.Writes,Is.Zero);Assert.That(s.Cache,Is.SameAs(original));await Task.CompletedTask;
    }
    [Test] public async Task ValidEnvelopeWithInvalidRowsIsNotTrusted()
    {var s=new Storage{Cache=TableCacheCodec.Encode(Id,Encoding.UTF8.GetBytes("1,1"))};var table=await Loader(s).LoadAsync(Id);Assert.That(s.Reads,Is.EqualTo(1));Assert.That(table.Rows.Count,Is.EqualTo(3));}
    [Test] public async Task DiskWriteFailureDoesNotDiscardValidatedTable()
    {var s=new Storage{ThrowWrite=true};var errors=new List<Exception>();var table=await Loader(s,errors.Add).LoadAsync(Id);Assert.That(table.Rows.Count,Is.EqualTo(3));Assert.That(errors.Count,Is.EqualTo(1));}
    [Test] public async Task CallerCancellationSkipsStorageAndSource()
    {var s=new Storage();Assert.CatchAsync<OperationCanceledException>(async()=>await Loader(s).LoadAsync(Id,new CancellationToken(true)));Assert.That(s.Reads,Is.Zero);await Task.CompletedTask;}
    [Test] public void SnapshotCopiesListAndRejectsDuplicateKeys()
    {
        var rows=new List<int>{1,2};var table=new TableSnapshot<int,int>(Id,rows,x=>x);rows[0]=99;Assert.That(table.Rows[0],Is.EqualTo(1));
        Assert.Throws<ArgumentException>(()=>new TableSnapshot<int,int>(Id,new[]{1,1},x=>x));
    }
    [Test] public void MalformedUtf8IsRejected()
    {
        var b=TableCacheCodec.Encode(Id,new byte[]{1});b[8]=255;Assert.That(TableCacheCodec.TryDecode(b,Id,out _),Is.False);
    }
}
