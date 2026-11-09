# Juahn V2 Tables

Pure C# / .NET Standard 2.1 / Unity 6000.0+ / MIT. No game rows, serializers, Unity or Sim references.

`TableIdentity` pins a name, content version and schema hash. `TableLoader<TKey,TRow>` first checks an exact-version cache envelope, then fetches from `ITableSource`. Payloads are decoded and duplicate/null keys rejected before replacing a cache. Cache I/O failure can be reported without discarding a validated source table. Cancellation propagates and source failures are not silently replaced with stale data.

`ITableDecoder<TRow>` or `DelegateTableDecoder<TRow>` adapts the application's CSV/JSON/binary reader. `ITableCache` adapters must replace bytes atomically. `TableSnapshot` copies and exposes a read-only row list and key index; rows themselves must be immutable application objects. Convert snapshots to game POCO config and implement `Juahn.V2.Sim.IConfigProvider` in the game, preserving Sim's package independence.

The JTB1 envelope is an independent cache format: exact identity, bounded UTF-8 fields, length, SHA-256 and payload. It rejects corruption, truncation, extra bytes and version/schema mismatches. A checksum does not authenticate a remote source; trust/transport and content rollout policy belong to the application. The default payload limit is 64 MiB. Encryption, remote configuration, Google Sheets access and game table generation are outside this core.

Create one immutable configuration snapshot per game session; do not replace battle configuration midway through a deterministic run. Binary compression and lazy row materialization should be added only after measuring a real startup or memory cost.

Run `dotnet test Tests~/dotnet/Tests.csproj` and `dotnet build Tools~/compile-check/Runtime.csproj`.
