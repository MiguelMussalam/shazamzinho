# Shazam-ESP32 (Shazamzin)

Reconhecimento de música no estilo Shazam: um **ESP32** captura áudio por microfone
MEMS, calcula fingerprints acústicos localmente (lib **Olaf**) e envia os hashes via
HTTP para um **backend .NET 10** que faz o matching contra um índice invertido no
**Valkey**, retornando os metadados da música.

> Documento de arquitetura completo e fonte da verdade: [`docs/architecture.md`](docs/architecture.md).

## Componentes

| Parte | Stack | Pasta |
|---|---|---|
| Firmware | ESP-IDF 5.4 (C) via PlatformIO | [`firmware/`](firmware/) |
| Backend / API | ASP.NET Core 10 Minimal API (C#) | [`backend/`](backend/) |
| Índice de fingerprints | Valkey 8 (Docker) | — |
| Metadados | SQLite + Dapper | [`backend/data/`](backend/) |
| Ingestão offline | Console .NET 10 + Olaf via P/Invoke | [`backend/src/Shazamzin.Ingestion/`](backend/src/Shazamzin.Ingestion/) |
| Lib nativa Olaf | C compilado como DLL | [`native/`](native/) |

## Estado atual — backend real e primeiro marco da Fase 3

Implementado e validado:

- ✅ Monorepo + git inicializado
- ✅ Solution C# (`Shazamzin.slnx`) com 4 projetos + testes, `global.json` pinado em .NET 10
- ✅ `GET /health`, `POST /match`, API key e documentação Scalar em `/docs`
- ✅ Matching real: índice Valkey, scoring por alinhamento temporal e metadados SQLite
- ✅ Olaf nativo fixado por commit, integrado ao .NET via P/Invoke
- ✅ CLI `add` para um arquivo e `extract` para consultas binárias de trechos reais
- ✅ Valkey 8 rodando em Docker (`scripts/setup-valkey.ps1`), `PING` → `PONG`
- ✅ Schema SQLite aplicado (`scripts/apply-sqlite-schema.ps1`)

Próximos marcos: ingestão de pastas e retomada automática, integração Olaf no ESP32,
captura pelo INMP441 e resultado no OLED. A validação de áudio real está em
[`docs/ingestion.md`](docs/ingestion.md); o histórico fica em [`docs/sprint-log.md`](docs/sprint-log.md).

## Começar

### Backend

```powershell
# subir o Valkey
./scripts/setup-valkey.ps1

# aplicar o schema do SQLite
./scripts/apply-sqlite-schema.ps1

# rodar a API
cd backend
dotnet run --project src/Shazamzin.Api
# em outro terminal:
curl http://localhost:5184/health
```

Em `Development`, a documentação interativa **Scalar** fica em
[http://localhost:5184/docs](http://localhost:5184/docs), e o documento OpenAPI em
[http://localhost:5184/openapi/v1.json](http://localhost:5184/openapi/v1.json).
Os perfis de execução abrem `/docs` ao iniciar pelo IDE. A interface e o documento
OpenAPI não são expostos em `Production`.

Para testar `POST /match` no Scalar, preencha a autenticação **ApiKey** com o valor
configurado em `Auth:ApiKey` e selecione um arquivo `.bin` como corpo
`application/octet-stream` (bytes brutos, sem JSON/base64). `X-Device-ID` é opcional.
Este exemplo PowerShell cria um payload válido vazio, que retorna `match: false`:

```powershell
$payloadPath = Join-Path $env:TEMP 'shazamzin-empty.bin'
[System.IO.File]::WriteAllBytes($payloadPath, [byte[]](1, 0, 0, 0, 1, 0, 0, 0))
$payloadPath # selecione este arquivo no cliente de testes do Scalar
```

Com Valkey indisponível, `/health` retorna `503` e as consultas reais não funcionam.
Para explorar a interface sem infraestrutura, rode o provider de demonstração:

```powershell
dotnet run --project src/Shazamzin.Api -- --Matching:Provider=Fake
```

O modo `Fake` usa respostas simuladas. Para o contrato completo do payload, veja
[`docs/protocol.md`](docs/protocol.md) e [`scripts/send-test-payload.ps1`](scripts/send-test-payload.ps1).

### Ingestão de áudio real

Compile a biblioteca com `./native/build.ps1` e depois rode
`dotnet build backend/Shazamzin.slnx`. Siga [`docs/ingestion.md`](docs/ingestion.md)
para criar um catálogo separado, indexar um arquivo e gerar um `.bin` reconhecível
pela API. O catálogo simulado da Fase 2 é preservado e não é misturado automaticamente
com os fingerprints reais.

### Firmware

Consulte o [guia de montagem no Fritzing](docs/fritzing/README.md) para posicionar os módulos e os jumpers.

Requer a toolchain ESP-IDF via PlatformIO. Ver
[`docs/esp-idf-notes.md`](docs/esp-idf-notes.md) e [`firmware/README`](firmware/).
Copie `firmware/main/config.h.example` para `firmware/main/config.h` e preencha as
credenciais antes de compilar.

## Fases do projeto

Ver seção 8 de [`docs/architecture.md`](docs/architecture.md). Cada fase termina com
algo demonstrável; o diário de bordo fica em [`docs/sprint-log.md`](docs/sprint-log.md).
