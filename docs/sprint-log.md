# Diário de bordo — Shazamzin

Atualize ao final de cada sessão: o que ficou pronto, decisões, o que falta, bugs.

---

## 2026-11-07 — Fase 0 (setup e familiarização)

### Feito e verificado nesta máquina

- Monorepo criado conforme seção 7 do `architecture.md`; `git init` (branch `main`).
- `architecture.md` movido de raiz para `docs/architecture.md`.
- Solution C# com os 4 projetos + testes, referências entre camadas ligadas:
  - `Shazamzin.Core`, `Shazamzin.Infrastructure` (→ Core), `Shazamzin.Api`
    (→ Core+Infra), `Shazamzin.Ingestion` (→ Core+Infra), `Shazamzin.Tests`.
  - `global.json` pinado em .NET 10 (`10.0.100`, rollForward `latestFeature`).
  - **Build da solution: 0 erros, 0 avisos.**
- Pacotes NuGet adicionados: Api (Serilog + sinks Console/File), Infrastructure
  (StackExchange.Redis, Microsoft.Data.Sqlite, Dapper), Tests (FluentAssertions).
- `GET /health` implementado e testado → `200 {"status":"ok","valkey":"unknown",
  "sqlite":"unknown","indexed_songs":0}`. (valkey/sqlite ainda "unknown"; viram check
  real na Fase 2.)
- `appsettings.json` preenchido conforme seção 10.2.
- Valkey 8.1.11 rodando em Docker via `scripts/setup-valkey.ps1` → `PING` = `PONG`;
  `maxmemory-policy=noeviction`, `save=900 1 300 100` confirmados.
- Schema SQLite aplicado via `scripts/apply-sqlite-schema.ps1` → tabelas `songs`,
  `ingestion_log`, `match_log` criadas. Schema fonte: `Shazamzin.Infrastructure/Sqlite/schema.sql`.
- Scaffolding criado (compila/roda só quando a toolchain existir): `firmware/`
  (platformio.ini, CMakeLists, main.c com Wi-Fi + stubs das tasks, config.h.example),
  `native/` (CMakeLists + build.ps1), docs (`protocol.md`, `valkey-schema.md`,
  `esp-idf-notes.md`), scripts (`setup-cloudflared.md`, `ingest-folder.ps1`),
  `README.md`, `.gitignore`.

### Decisões

- **Solution em formato `.slnx`** (XML), padrão do `dotnet new sln` no .NET 10, em vez
  de `.sln`. O `architecture.md` cita `Shazamzin.sln`; funciona igual, `dotnet build`
  acha automático. Manter `.slnx`.
- Valkey com `appendonly no` (só RDB) — reconstruível via re-ingestão, RDB basta.
- Pacotes de Ingestão (z440.atl.core, System.CommandLine, Spectre.Console) **adiados**
  para a Fase 3 — Ingestion é só um stub console por enquanto, evita puxar pacotes
  preview sem uso.
- Serilog instalado mas ainda **não** cabeado no `Program.cs` (fica pra Fase 1); health
  usa logging default.

### Pendente da Fase 0 (bloqueado por ferramentas/hardware ausentes)

Nesta máquina faltam: `pio`/toolchain ESP-IDF, `cmake`, `ffmpeg`, `cloudflared`, e o
hardware (ESP32 + INMP441).

- [ ] Instalar extensão PlatformIO/ESP-IDF; rodar tutorial blink + hello world serial.
- [ ] Compilar o firmware mínimo (Wi-Fi + IP no serial) numa placa real.
- [ ] Clonar e compilar o Olaf standalone (precisa de cmake) e rodar o CLI numa música
      de teste para conferir o algoritmo isolado.
- [ ] Docker Desktop precisou ser iniciado manualmente — garantir que sobe antes de rodar.

### Próximos passos sugeridos

1. Instalar as ferramentas pendentes acima.
2. Iniciar a **Fase 1** (backend fake + protocolo): modelos no Core, `FingerprintCodec`
   com testes de round-trip, `POST /match` com `IMatchingService` fake, middleware de
   API key, Serilog cabeado, OpenAPI. Ver `docs/protocol.md`.

### Bugs conhecidos

- Nenhum.

---

## 2026-11-07/08 — Hardware, diagrama e retomada do firmware

### Feito

- **PlatformIO Core 6.2.0** instalado (venv em `C:\Users\migue\.platformio\penv`; `pio` não está no PATH, usar caminho completo).
- **ESP32 detectado em COM8** (USB-Enhanced-SERIAL CH9102).
- **Decisão de hardware:** OLED SSD1306 I²C (SDA=GPIO21, SCL=GPIO22) promovido a peça essencial — display de 7 segmentos não escreve título/artista. `architecture.md` atualizado (§3.3, §4.1, §4.4, §4.5).
- **Planta Fritzing** pronta em `docs/fritzing/Shazamzin.fzz` (peças reais da comunidade, 12 ligações). Validada pelo usuário no Fritzing 0.9.3b; precisou importar os 3 `.fzpz` antes. Re-salva pelo Fritzing com as 3 views.
- Diagrama web de ligação publicado como Artifact (versão pictórica com protoboard).
- `platformio.ini`: adicionado `src_dir = main` (o código fica em `main/`, não `src/`).
- `.gitignore`: ignora o Fritzing portátil (`fritzing.*/`, 364 MB, estava dentro do repo — mover pra fora).

### Combinado

- Sem o microfone físico ainda: na Fase 4 a gente alimenta um WAV bruto no pipeline de fingerprint/rede do ESP real (equivale ao "teste de consistência" do plano).

### Pendente

- [ ] Primeira build do firmware (baixa o toolchain ESP-IDF; ainda não compilou nunca).
- [ ] Gravar na COM8 e ver o IP no serial (precisa SSID/senha reais em `firmware/main/config.h`).
- [ ] Instalar cmake, ffmpeg, cloudflared (winget).
- [ ] Docker Desktop precisa estar aberto antes do `setup-valkey.ps1` (o container não sobrevive a reinício do Docker fechado).
