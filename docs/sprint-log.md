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

- [ ] Gravar com SSID/senha **reais** em `firmware/main/config.h` e ver o IP no serial (hoje o `config.h` tem credenciais falsas, só pra validar build/gravação).
- [ ] Docker Desktop precisa estar aberto antes do `setup-valkey.ps1` (e o container fica parado se o Docker fechou: `docker start shazamzin-valkey`).

---

## 2026-11-08 — Firmware validado em hardware, Fase 1 e Fase 2 do backend

### Firmware (Fase 0, parte de hardware)

- cmake, ffmpeg e cloudflared instalados via winget (precisam de terminal novo pra entrar no PATH).
- **1ª build real**: o PlatformIO pegou a plataforma `espressif32@8.0.0` = **ESP-IDF 6.1**, onde o componente `json` (cJSON) não existe mais. Corrigido pinando `platform = espressif32@6.11.0` (= **ESP-IDF 5.4.1**, igual ao plano).
- Build: ~22 s incremental, RAM 10%, Flash 71%. `sdkconfig.defaults` aplicado (partição `singleapp_large`).
- **Gravado na COM8 e testado em hardware real**: boot OK, 276 KB de heap livre, Wi-Fi STA sobe, retry (10x) e caminho de falha funcionam. Testado com SSID falso (credenciais reais ainda pendentes).
- Corrigido log enganoso: não imprime mais "Fase 0 OK" quando o Wi-Fi falha.

### Fase 1 — backend fake + protocolo (branch `feature/fase-1-backend-fake`, mergeada)

- Core: `Fingerprint`, `FingerprintPayload`, `Song`, `MatchResult`, `IMatchingService`, `FingerprintCodec` (little-endian, validação de versão/tamanho/count).
- Api: `POST /match` (octet-stream → JSON snake_case), `ApiKeyAuthMiddleware` (tempo constante), Serilog (console + arquivo, `RequestId`/`DeviceId`), OpenAPI 3.1 em `/openapi/v1.json` (só Development). Códigos 200/400/401/413/415.
- Decisão: `count == 0` é aceito e vira no-match (registrado em `protocol.md`).
- `scripts/send-test-payload.ps1` para teste manual sem o ESP.

### Fase 2 — matching real (branch `feature/fase-2-matching-real`)

- `MatchScorer` (Core, função pura): histograma de deltas com bucket de 50 ms, desempate determinístico (mais votos → menor song_id → menor bucket). `confidence = score / hashes_hit` (limitado a 1); `matched_offset_sec` = média dos deltas no pico.
- `PostingsCodec` (blob de 8 bytes/posting, `fp:{hash:x8}`), `ValkeyMatchingService` (um MGET em lote), `SqliteSongRepository` (Dapper), `SqliteInitializer` (cria o banco e aplica o `schema.sql` embutido; resolve o caminho relativo a partir do `.slnx`, então o banco é sempre `backend/data/shazamzin.db`).
- `/health` real: PING no Valkey + COUNT no SQLite; 200 se tudo up, **503 `degraded`** se algo caiu (e diz qual).
- `Matching:Provider` = `Valkey` (padrão) ou `Fake` (Fase 1) em `appsettings.json`; a API sobe mesmo com o Valkey fora do ar (`abortConnect=false`).
- **52 testes, 0 pulados**: scorer, codec, SQLite, serviço contra Valkey real (database 15, esvaziado por teste), API ponta a ponta e `/health` com Valkey fora do ar. O `[ValkeyFact]` pula sozinho se o Valkey não estiver de pé.
- `scripts/seed-fake-data.ps1`: 3 músicas fake (4.500 chaves) + payloads de consulta com 20% de ruído. Validado: as 3 identificadas corretamente; consulta só de ruído = no-match.
- **Benchmark** (`scripts/benchmark-match.ps1`, 30 req/tamanho): 100 hashes ≈ 2 ms (rtt p50), 1.000 ≈ 3 ms, 10.000 ≈ 48 ms. Um ESP manda ~100–200 hashes → matching bem abaixo dos 15–30 ms do plano. Ressalva: catálogo de teste tem só 4.500 chaves; a escala final (milhões) muda a RAM, não o custo por chave.
- Bugs achados e corrigidos no caminho: `$PSScriptRoot` não vale dentro dos defaults do `param()` no PowerShell 5.1 (afetava também o `apply-sqlite-schema.ps1` da Fase 0); literal `0xA3F29B01` vira Int32 negativo em PowerShell (usar sufixo `L`); `is_healthy` vazando no JSON do `/health`.

### Próximos passos

1. Credenciais reais de Wi-Fi → ver o IP no serial (fecha a Fase 0 de vez).
2. **Fase 3**: compilar o Olaf (cmake já instalado), P/Invoke, ingestão real (ffmpeg já instalado).
3. Fase 4: com o ESP real e sem o mic, alimentar um WAV bruto no pipeline.
