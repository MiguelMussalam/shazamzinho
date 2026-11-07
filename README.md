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

## Estado atual — Fase 0 (setup)

Concluído e verificado nesta máquina:

- ✅ Monorepo + git inicializado
- ✅ Solution C# (`Shazamzin.slnx`) com 4 projetos + testes, `global.json` pinado em .NET 10
- ✅ `GET /health` → `200 {"status":"ok",...}`
- ✅ Valkey 8 rodando em Docker (`scripts/setup-valkey.ps1`), `PING` → `PONG`
- ✅ Schema SQLite aplicado (`scripts/apply-sqlite-schema.ps1`)

Pendente (requer ferramentas/hardware ainda não instalados): toolchain ESP-IDF/PlatformIO,
compilação standalone do Olaf (cmake), `ffmpeg`, `cloudflared`. Ver [`docs/sprint-log.md`](docs/sprint-log.md).

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

### Firmware

Requer a toolchain ESP-IDF via PlatformIO (ainda não instalada). Ver
[`docs/esp-idf-notes.md`](docs/esp-idf-notes.md) e [`firmware/README`](firmware/).
Copie `firmware/main/config.h.example` para `firmware/main/config.h` e preencha as
credenciais antes de compilar.

## Fases do projeto

Ver seção 8 de [`docs/architecture.md`](docs/architecture.md). Cada fase termina com
algo demonstrável; o diário de bordo fica em [`docs/sprint-log.md`](docs/sprint-log.md).
