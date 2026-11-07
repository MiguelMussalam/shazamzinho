# Protocolo binário ESP ↔ servidor

Contrato detalhado do payload enviado pelo ESP32 no `POST /match`.
Resumo em `architecture.md` seção 5.1 — este documento é a referência de implementação
compartilhada entre `firmware/main/protocol.c` e `Shazamzin.Core/Codec/FingerprintCodec.cs`.

## Visão geral

```
Payload = header (8 bytes) + N × fingerprint_entry (8 bytes)
```

**Toda a codificação é little-endian.** O ESP32 (Xtensa) já é little-endian, então
`memcpy` direto funciona; no C# usar `BinaryPrimitives.*LittleEndian`.

## Header (8 bytes)

| Offset | Tipo    | Campo              | Valor / observação                  |
|-------:|---------|--------------------|-------------------------------------|
| 0      | uint8   | `protocol_version` | = 1                                 |
| 1      | uint8   | `reserved`         | = 0 (padding / uso futuro)          |
| 2      | uint16  | `count`            | N = número de fingerprint_entry     |
| 4      | uint32  | `session_id`       | identificador do request            |

## fingerprint_entry (8 bytes)

| Offset | Tipo    | Campo          | Observação                                  |
|-------:|---------|----------------|---------------------------------------------|
| 0      | uint32  | `hash`         | hash combinatorial do Olaf                  |
| 4      | uint32  | `timestamp_ms` | ms relativo ao início da captura            |

Tamanho total do payload: `8 + 8 * N` bytes.

## Validações do servidor (decode)

O decoder deve rejeitar (HTTP 400) quando:

- `protocol_version != 1`
- o corpo tem menos de 8 bytes (header incompleto)
- `body.Length != 8 + 8 * count` (count não bate com o tamanho real)
- `count == 0` (nada a casar) — ou tratar como no-match, decidir na Fase 1

## Headers HTTP

```http
POST /match HTTP/1.1
Content-Type: application/octet-stream
X-Device-ID: esp32-01
X-Api-Key: <key do appsettings>
```

Ver `architecture.md` seção 5.2 para os formatos de resposta JSON (match / no-match).

## Notas de implementação

- No ESP32, montar o buffer com um `struct __attribute__((packed))` **não** é garantido
  portável; preferir escrita campo a campo em offsets fixos (como a tabela acima).
- No C#, testar round-trip encode→decode, limites de `count` (0, 1, 65535) e header
  inválido — é um item explícito da Fase 1.
