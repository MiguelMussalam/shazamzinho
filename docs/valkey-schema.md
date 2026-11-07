# Schema do Valkey

Índice invertido de fingerprints. Resumo em `architecture.md` seção 5.3.

## Chave

```
fp:{hash_hex}      ex: fp:a3f29b01
```

`hash_hex` = o `uint32` do hash do Olaf em hexadecimal (8 dígitos, minúsculas).

## Valor

Blob binário: lista concatenada de postings, cada posting com 8 bytes:

| Offset | Tipo         | Campo       |
|-------:|--------------|-------------|
| 0      | int32 LE     | `song_id`   |
| 4      | int32 LE     | `offset_ms` |

Um mesmo hash pode apontar para muitas músicas/offsets → o valor cresce por `APPEND`
(8 bytes por posting). Decode: ler em blocos de 8 bytes. Ver `PostingsCodec` (Fase 2).

## Configuração do servidor

- Sem TTL nas chaves `fp:*`
- `maxmemory 9gb`
- `maxmemory-policy noeviction` — rejeita writes quando cheio, **não** apaga dados
- Persistência RDB: `save 900 1`, `save 300 100`
- `appendonly no` (RDB basta para este caso; reconstruível via re-ingestão)

Tudo isso é aplicado por `scripts/setup-valkey.ps1`.

## Ingestão (write path)

Para cada `(hash, offset_ms)` extraído de uma música com `song_id`:

```
APPEND fp:{hash_hex} <8 bytes: song_id LE || offset_ms LE>
```

Em lote via `IBatch` do StackExchange.Redis (Fase 3).

## Matching (read path)

1. `MGET fp:{h1} fp:{h2} ... fp:{hN}` para os hashes recebidos do ESP
2. Para cada hit, decodificar os postings
3. Scoring por histograma de deltas `(offset_db - timestamp_query)`, bucket de 50 ms
4. `song_id` com maior pico acima de `MinScoreThreshold` vence (Fase 2)

## Dimensionamento

~1.500 tuplas/música. 5k músicas ≈ 7,5M postings ≈ ~800 MB RAM.
Ver `shazamzin-ingest stats` para números reais (Fase 3).
