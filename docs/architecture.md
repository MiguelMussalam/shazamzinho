# Plano de Projeto: Shazam-ESP32

**Documento de arquitetura e referência de implementação**
**Autor:** Ronaldo
**Última atualização:** 2026-11-08
**Status:** Backend real (Fase 2) e primeiro marco da Fase 3 implementados; ingestão em lote e firmware de áudio pendentes

---

## 1. Visão geral

Sistema de reconhecimento de música no estilo Shazam, com extração de fingerprints acústicos rodando localmente num ESP32 e um banco de fingerprints de grande escala (5–50 mil músicas) hospedado num servidor doméstico, acessível tanto em rede local quanto publicamente via túnel. O ESP32 captura áudio via microfone MEMS, calcula hashes do algoritmo de audio fingerprinting (constellation map + hashing combinatorial, inspirado no Shazam/Olaf), envia os hashes via HTTP para o servidor, que faz o matching contra um índice invertido em Valkey e retorna os metadados da música identificada.

**Objetivo prático:** demonstração funcional em sala de aula, reconhecendo músicas de um catálogo pessoal de milhares de títulos pré-indexado, com latência fim-a-fim de ~3-5 segundos.

**Stack decidido:**
- **Firmware:** ESP-IDF 5.4 via PlatformIO, C puro, com algoritmo de fingerprint do Olaf linkado como componente
- **Backend:** ASP.NET Core 10 Minimal API, C# .NET 10 (LTS)
- **Índice de fingerprints:** Valkey 8 (fork OSS do Redis, mesmo protocolo RESP)
- **Metadados:** SQLite com Dapper
- **Ingestão:** Console app em .NET 10 que linka a lib Olaf via P/Invoke
- **Rede:** Cloudflare Tunnel como padrão + hotspot do celular como fallback para apresentação

---

## 2. Dimensionamento

### 2.1 Alvo de catálogo

- **Mínimo para apresentação:** 5.000 músicas (~7.5M tuplas no Valkey, ~800 MB RAM)
- **Alvo confortável:** até 50.000 músicas (~8 GB RAM, dentro dos 10 GB disponíveis no notebook)
- **Teto do notebook:** ~60-70k músicas antes de precisar otimizar encoding

### 2.2 Latência esperada

| Etapa | Tempo |
|---|---|
| Captura de áudio (ESP32) | ~3 s (dominante) |
| Processamento DSP + fingerprinting | paralelo à captura |
| Serialização + envio Wi-Fi | ~50-150 ms |
| Matching no servidor (Valkey MGET + scoring) | ~15-30 ms |
| Lookup de metadados (SQLite) | <1 ms |
| Resposta HTTP + parse no ESP | ~50 ms |
| **Total fim-a-fim** | **~3.2-3.5 s** |

---

## 3. Arquitetura

### 3.1 Visão macro

```
┌─────────────────────────────┐
│       ESP32 (clássico)      │
│                             │
│  INMP441 ──I2S──▶ AudioCap  │
│                     │       │
│                     ▼       │
│                Fingerprint  │
│                 (Olaf lib)  │
│                     │       │
│                     ▼       │
│                 Network     │
│               (esp_http)    │
└──────────────┬──────────────┘
               │ Wi-Fi
         [Roteador casa]
               │
               ▼
      [cloudflared tunnel]
               │
      https://xxx.trycloudflare.com
               │
               ▼
┌─────────────────────────────┐
│       Notebook (casa)       │
│                             │
│  ASP.NET Core 10 ─┬──▶ Valkey│
│  POST /match      │         │
│                   └──▶SQLite│
│                             │
│  [offline, roda sob demanda]│
│  Shazamzin.Ingestion.exe    │
│  (popula Valkey + SQLite)   │
└─────────────────────────────┘
```

### 3.2 Decisões arquiteturais

1. **Clean Architecture leve** no backend, seguindo o padrão do estágio: Interface + Implementação direta de serviços, sem Factory
2. **FreeRTOS multi-task no ESP32** com filas entre captura, DSP e rede
3. **Lib Olaf em C compartilhada** entre firmware (componente ESP-IDF) e ingestão (via P/Invoke DLL) — garante fingerprint bit-a-bit idêntico nos dois lados
4. **HTTP + payload binário** para MVP; WebSocket + Protobuf como evolução futura
5. **Cloudflare Tunnel** para exposição pública, mantendo banco em casa
6. **IP fixo via reserva DHCP** para fallback em LAN; hotspot do celular como plano B na apresentação
7. **Ingestão idempotente e resumível** — pré-requisito dado o volume de 5k+ músicas

### 3.3 Divisão de responsabilidades no ESP32

```
Core 0 (sistema)              Core 1 (aplicação)
├── Wi-Fi stack               ├── AudioCaptureTask    (prio alta)
├── TCP/IP                    ├── FingerprintTask     (prio média)
└── NetworkTask               └── UITask (OLED + serial)  (prio baixa)
```

**Fluxo:** I2S DMA → AudioCaptureTask → fila_samples → FingerprintTask → fila_fingerprints → NetworkTask → HTTP POST → UITask mostra título + artista no OLED e imprime no Serial.

**Máquina de estados:** `IDLE → CAPTURING → PROCESSING → QUERYING → RESULT → IDLE`

### 3.4 Divisão de camadas no backend

```
Shazamzin.sln
├── src/
│   ├── Shazamzin.Api/              # ASP.NET Core 10 Minimal API
│   ├── Shazamzin.Core/             # Domínio: modelos, interfaces, codec compartilhado
│   ├── Shazamzin.Infrastructure/   # Valkey, SQLite, P/Invoke Olaf
│   └── Shazamzin.Ingestion/        # Console app offline
└── tests/
    └── Shazamzin.Tests/            # xUnit + FluentAssertions
```

---

## 4. Hardware

### 4.1 Essencial (fase MVP)

- **ESP32 clássico (WROOM-32)** — já disponível
- **Microfone INMP441** em breakout board I2S (~R$30)
- **Protoboard 400 pontos + jumpers macho-fêmea** (~R$30)
- **Cabo USB** de qualidade
- **Display OLED SSD1306 0.96" 128x64 I2C** — mostra título e artista da música reconhecida (decisão de 2026-11-07: promovido de nice-to-have para essencial; um display de 7 segmentos não escreve texto, por isso o OLED)

### 4.2 Nice-to-have (depois do MVP funcionar)

- LED RGB para status
- Botão push para trigger manual (o botão BOOT da placa já serve pra prototipagem)
- Case 3D impresso se for levar portátil

### 4.3 Pinagem INMP441 ↔ ESP32

| INMP441 | ESP32 | Função |
|---|---|---|
| VDD | 3.3V | Alimentação |
| GND | GND | Terra |
| L/R | GND | Seleciona canal esquerdo |
| WS (LRCLK) | GPIO 15 | Word Select |
| SCK (BCLK) | GPIO 14 | Bit Clock |
| SD (DATA) | GPIO 32 | Data out |

O GPIO 15 é um pino de strapping do ESP32 com pull-up interno. O `WS` do INMP441 é uma entrada. Confira o boot com o módulo conectado. [Datasheet ESP32](https://documentation.espressif.com/esp32_datasheet_en.html)

### 4.4 Pinagem OLED SSD1306 (I²C) ↔ ESP32

| OLED | ESP32 | Função |
|---|---|---|
| VCC | 3.3V | Alimentação |
| GND | GND | Terra |
| SDA | GPIO 21 | Dados I²C |
| SCL | GPIO 22 | Clock I²C |

Endereço I²C típico: `0x3C`. Alimentação (3.3V/GND) de OLED e INMP441 sai dos trilhos da protoboard.

### 4.5 Diagramas de montagem

- Montagem em protoboard disponível em [arquivo editável do Fritzing](fritzing/Shazamzin.fzz) e [vista exportada](fritzing/Shazamzin_bb.svg). Os ajustes e a pinagem estão no [guia de montagem](fritzing/README.md).

---

## 5. Contratos entre componentes

### 5.1 Formato binário do fingerprint (ESP ↔ servidor)

```
Payload = header + N × fingerprint_entry

header (8 bytes):
  uint8  protocol_version   // = 1
  uint8  reserved           // = 0
  uint16 count              // N fingerprints
  uint32 session_id         // identificador do request

fingerprint_entry (8 bytes):
  uint32 hash               // hash combinatorial
  uint32 timestamp_ms       // timestamp relativo ao início da captura

Tudo little-endian.
```

### 5.2 API HTTP

**Request:**
```http
POST /match HTTP/1.1
Content-Type: application/octet-stream
X-Device-ID: esp32-01
X-Api-Key: <key_do_appsettings>

[payload binário]
```

**Response (match):**
```json
{
  "match": true,
  "song_id": 42,
  "title": "Redemption Song",
  "artist": "Bob Marley",
  "album": "Uprising",
  "confidence": 0.87,
  "matched_offset_sec": 47.3,
  "processing_time_ms": 68
}
```

**Response (no match):**
```json
{
  "match": false,
  "processing_time_ms": 54,
  "hashes_received": 180,
  "hashes_hit": 12
}
```

**Health check:**
```
GET /health → 200 {"status":"ok","valkey":"up","sqlite":"up","indexed_songs":5230}
```

### 5.3 Schema do Valkey

- **Chave:** `fp:{hash_hex}` (ex: `fp:a3f29b01`)
- **Valor:** blob binário, lista concatenada de `(song_id: int32 LE, offset_ms: int32 LE)`
- Sem TTL
- `maxmemory 9gb`, `maxmemory-policy noeviction` (rejeita writes em vez de apagar dados)
- Persistência: RDB snapshot (`save 900 1`, `save 300 100`)

### 5.4 Schema do SQLite

```sql
CREATE TABLE songs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  title TEXT NOT NULL,
  artist TEXT NOT NULL,
  album TEXT,
  duration_sec INTEGER,
  file_path TEXT NOT NULL,
  file_hash TEXT NOT NULL UNIQUE,     -- SHA256 do arquivo, pra idempotência
  fingerprint_count INTEGER,
  added_at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX idx_songs_file_hash ON songs(file_hash);

CREATE TABLE ingestion_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  file_path TEXT NOT NULL,
  file_hash TEXT,
  song_id INTEGER REFERENCES songs(id),
  started_at TEXT NOT NULL,
  finished_at TEXT,
  status TEXT NOT NULL,   -- 'pending' | 'done' | 'skipped' | 'failed'
  error_message TEXT
);
CREATE INDEX idx_ingestion_log_status ON ingestion_log(status);

CREATE TABLE match_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  device_id TEXT NOT NULL,
  matched_song_id INTEGER REFERENCES songs(id),
  confidence REAL,
  hashes_received INTEGER,
  hashes_hit INTEGER,
  processing_time_ms INTEGER,
  timestamp TEXT NOT NULL DEFAULT (datetime('now'))
);
```

**file_hash** serve pra deduplicar: se um arquivo já foi ingerido, pula; se é a mesma música mas de arquivo diferente, trata como item novo (metadados podem variar).

---

## 6. Pipeline de ingestão (com 5k+ músicas)

### 6.1 Requisitos funcionais

- **Resumível:** se interromper no meio de 5k, continua de onde parou
- **Idempotente:** reprocessar o mesmo arquivo não duplica dados
- **Paralelo:** processar N arquivos simultaneamente (configurável, default = Nº de cores)
- **Robusto:** um arquivo corrompido não para o pipeline
- **Observável:** barra de progresso + log estruturado por arquivo
- **Reversível:** comando `--remove` que apaga uma música do Valkey e SQLite

### 6.2 Fluxo por arquivo

```
1. Calcula SHA256 do arquivo
2. Consulta SQLite: já existe song com esse file_hash? → pula
3. Insere linha em ingestion_log (status=pending)
4. Decodifica áudio: ffmpeg → WAV mono 8kHz num tmpfile
5. Extrai metadados com ATL.NET
6. Chama Olaf via P/Invoke → recebe array de (hash, offset_ms)
7. Insere song no SQLite → recebe song_id
8. Pipeline Valkey: para cada hash, executa APPEND fp:{hash} <blob>
9. Atualiza ingestion_log (status=done, finished_at=now)
10. Se qualquer passo falhar: ingestion_log.status=failed, error_message, continua próximo
```

### 6.3 CLI proposta

```
shazamzin-ingest add --path "C:\Musicas"                     # ingere pasta recursiva
shazamzin-ingest add --path "arquivo.mp3" --concurrency 8
shazamzin-ingest resume                                       # processa pendentes/falhos
shazamzin-ingest remove --song-id 42                          # remove música
shazamzin-ingest stats                                        # imprime estatísticas do catálogo
shazamzin-ingest verify                                       # sanity check SQLite vs Valkey
```

### 6.4 Estimativa de tempo

- Decodificação + fingerprint por música (~3 min de áudio): 5-15 s dependendo do CPU
- Paralelismo 4x no notebook: ~2-4 músicas/s efetivas
- **5.000 músicas em ~30-60 minutos**
- **50.000 músicas em ~5-10 horas** (overnight)

---

## 7. Estrutura de repositório

Monorepo com três subprojetos:

```
shazam-esp32/
├── README.md
├── docs/
│   ├── architecture.md           # Este documento
│   ├── protocol.md               # Contrato binário detalhado
│   ├── valkey-schema.md
│   ├── esp-idf-notes.md          # Dicas específicas de ESP-IDF pra reusar depois
│   └── sprint-log.md             # Diário de bordo
│
├── firmware/                     # ESP32
│   ├── platformio.ini
│   ├── CMakeLists.txt
│   ├── sdkconfig.defaults        # Config padrão ESP-IDF
│   ├── main/
│   │   ├── CMakeLists.txt
│   │   ├── main.c                # app_main() — inicialização e tasks
│   │   ├── config.h              # SSID, URL servidor, API key (gitignored)
│   │   ├── audio_capture.c/.h
│   │   ├── fingerprint.c/.h
│   │   ├── network.c/.h
│   │   ├── protocol.c/.h         # Serialização binária
│   │   └── state_machine.c/.h
│   └── components/
│       └── olaf/                 # Olaf como componente ESP-IDF
│
├── backend/                      # C#
│   ├── Shazamzin.sln
│   ├── global.json               # Pin .NET 10
│   ├── src/
│   │   ├── Shazamzin.Api/
│   │   ├── Shazamzin.Core/
│   │   ├── Shazamzin.Infrastructure/
│   │   └── Shazamzin.Ingestion/
│   └── tests/
│       └── Shazamzin.Tests/
│
├── native/                       # Compilação da lib Olaf pra DLL
│   ├── CMakeLists.txt
│   └── build.ps1                 # Script pra gerar libolaf.dll
│
├── scripts/
│   ├── setup-valkey.ps1          # Docker run do Valkey configurado
│   ├── setup-cloudflared.md      # Guia de setup do túnel
│   └── ingest-folder.ps1         # Wrapper amigável do Ingestion
│
└── .gitignore
```

---

## 8. Plano de implementação em fases

Prazo-alvo: **apresentação em ~9-11 semanas**, com folga para imprevistos e para absorver ESP-IDF sem pressa. Cada fase termina com algo funcional e demonstrável. A ordem é propositalmente "backend antes de hardware" para evitar debugar duas pontas simultaneamente.

### Fase 0 — Setup e familiarização (semana 1-2)

**Entregável:** ambiente pronto, "hello world" rodando em ambas as pontas.

- [ ] Instalar .NET 10 SDK, VS Code / Rider, PlatformIO, Docker (ou Podman)
- [ ] Instalar extensão ESP-IDF no VS Code (ou via PlatformIO)
- [ ] Rodar tutorial oficial "ESP-IDF Getting Started" — blink LED + hello world no serial
- [ ] Criar monorepo com estrutura da seção 7 no GitHub
- [ ] `platformio.ini` configurado para ESP32 WROOM-32 com framework = espidf
- [ ] Firmware mínimo: conecta Wi-Fi via `esp_wifi`, imprime IP no serial com `ESP_LOGI`
- [ ] Solution C# com os 4 projetos criados, `global.json` pinado em .NET 10
- [ ] API responde `GET /health` → 200 ok
- [ ] Valkey 8 rodando em container Docker/Podman, testado com `redis-cli PING` (ainda funciona com Valkey)
- [ ] SQLite criado com schema da seção 5.4 aplicado via script
- [ ] Documento `docs/sprint-log.md` com notas da semana
- [ ] Compilar Olaf standalone e rodar o CLI em uma música de teste para gerar fingerprints — conferir que o algoritmo funciona isoladamente

### Fase 1 — Backend fake + protocolo (semana 3)

**Entregável:** servidor recebe payload binário, decodifica, retorna resposta mockada.

- [ ] `Shazamzin.Core`: modelos `Fingerprint`, `MatchResult`, `Song`
- [ ] `Shazamzin.Core/Codec/FingerprintCodec.cs`: encode/decode do formato binário
- [ ] Testes unitários do codec (round-trip, little-endian, limites, header inválido)
- [ ] `Shazamzin.Api`: endpoint `POST /match` que lê body binário, chama `IMatchingService` (implementação fake), retorna JSON
- [ ] Middleware `ApiKeyAuth`
- [ ] Serilog configurado (console + arquivo), com enrichers de request ID e device ID
- [ ] OpenAPI 3.1 habilitado (built-in no .NET 10)
- [ ] Teste manual com `curl` enviando payload binário gerado por script PowerShell

### Fase 2 — Matching real + Valkey (semana 4)

**Entregável:** servidor faz matching real contra dados fake inseridos manualmente no Valkey.

- [ ] `Shazamzin.Infrastructure/Valkey/ValkeyMatchingService.cs` com batch MGET (via StackExchange.Redis, compatível com Valkey)
- [ ] `Shazamzin.Infrastructure/Valkey/PostingsCodec.cs` (encode/decode do blob de valor)
- [ ] Algoritmo de scoring: histograma de deltas com bucket de 50ms
- [ ] Threshold configurável via `appsettings.json`
- [ ] `Shazamzin.Infrastructure/Sqlite/SqliteSongRepository.cs` com Dapper
- [ ] Script `scripts/seed-fake-data.ps1`: insere 3 músicas fake no SQLite + fingerprints simulados no Valkey
- [ ] Teste de integração: request manual retorna match da música seed
- [ ] Benchmark: medir latência do endpoint com 100, 1k, 10k hashes fake no catálogo

### Fase 3 — Compilação nativa do Olaf + ingestão (semana 5-6)

**Entregável:** ingestão real e robusta de 5k+ arquivos de áudio.

- [x] Olaf fixado por commit em `native/olaf.version`; perfil explícito 8 kHz / FFT 1024 / passo 128 / 2 event points
- [x] `native/CMakeLists.txt`: compila `libolaf.dll`; a ponte recebe PCM float32 e entrega fingerprints por callback
- [x] `native/build.ps1` compila a biblioteca; o build .NET copia para execução/publicação
- [x] `Shazamzin.Infrastructure/Native/OlafNative.cs` com `[DllImport]`
- [x] Teste: mesmo PCM via P/Invoke e CLI C da ponte produz fingerprints idênticos (comparação com ESP32 ainda pendente)
- [ ] `Shazamzin.Ingestion` console app com CLI da seção 6.3 (System.CommandLine)
- [x] Primeiro recorte da CLI: `add` de um arquivo e `extract` de trecho independente para payload v1
- [x] Decodificação: `ffmpeg` para PCM float32 LE mono 8 kHz, sem arquivo WAV intermediário
- [x] Metadados via `ffprobe`, que já acompanha FFmpeg; substitui ATL.NET nesta entrega
- [x] Idempotência via SHA256; repetição de `add` repara uma indexação parcial sem duplicar postings
- [ ] Paralelismo com `Parallel.ForEachAsync` e `Channel<T>` alimentando batch writer
- [x] Writes em grupos de 128 hashes, com Lua por chave substituindo somente postings do song ID em ingestão
- [ ] Comando `resume` que relê `ingestion_log` buscando pendentes/falhos
- [ ] Comando `stats`: total de músicas, total de hashes únicos, RAM estimada no Valkey
- [ ] **Milestone concreto:** ingerir 100 músicas de teste, validar com queries manuais

**Primeiro marco validado:** duas músicas reais indexadas em catálogo isolado;
cinco consultas de 3 s na primeira música reconheceram os offsets esperados.
O perfil e a identidade do catálogo são registrados no SQLite e no Valkey.
Músicas com ingestão `pending`/`failed` ficam indisponíveis até o reparo.
Instruções e limites em [`ingestion.md`](ingestion.md) e [`../native/README.md`](../native/README.md).

### Fase 4 — ESP32 áudio + Olaf local (semana 7-8)

**Entregável:** ESP32 captura áudio, extrai fingerprints localmente, imprime hashes no serial.

- [ ] Setup do hardware: INMP441 no protoboard, alimentação, pinagem da seção 4.3
- [ ] **Validação crítica:** usar o exemplo `esp32_inmp441_wifi_mic.ino` do Olaf (adaptado pra ESP-IDF) para enviar áudio via UDP e ouvir com `ffplay` — confirmar que áudio tá limpo, sem dropout nem clipping
- [ ] Integrar lib Olaf como componente em `firmware/components/olaf/`
- [ ] Task de captura I2S funcional com `i2s_std_new_channel` e DMA ring buffer
- [ ] Task de fingerprinting consumindo do ring buffer, chamando Olaf
- [ ] Imprimir hashes gerados no Serial via `ESP_LOGI` para validação visual
- [ ] **Teste de consistência (crítico):** comparar hashes gerados pelo ESP com hashes gerados pela ingestão no PC usando o mesmo áudio de referência (WAV gravado via UDP e salvo) — devem bater bit-a-bit
- [ ] Monitorar uso de heap com `esp_get_free_heap_size()` ao longo da execução

### Fase 5 — Integração ESP32 ↔ servidor (semana 9)

**Entregável:** reconhecimento fim-a-fim funcionando em LAN.

- [ ] Task de rede usando `esp_http_client`
- [ ] Serialização do payload binário no ESP (mesmo contrato do servidor)
- [ ] Desserialização da resposta JSON com `cJSON`
- [ ] Máquina de estados completa com trigger pelo botão BOOT (GPIO 0)
- [ ] Fluxo completo: aperta botão → captura 3s → envia → recebe match → imprime no Serial
- [ ] Ingerir o catálogo definitivo (5k+ músicas) em background enquanto desenvolve
- [ ] Testar reconhecimento de 20-30 músicas diferentes do catálogo
- [ ] Medir e documentar latência fim-a-fim

### Fase 6 — Tunelamento + preparação de apresentação (semana 10)

**Entregável:** sistema funcional remotamente, pronto para demonstração.

- [ ] Instalar e configurar `cloudflared` no notebook servidor
- [ ] Adicionar suporte a HTTPS no ESP32 (`esp_http_client` com cert bundle)
- [ ] Testar ESP32 conectado em hotspot do celular acessando a URL pública
- [ ] Testar sob condições adversas (outro Wi-Fi, outra rede, rede 4G)
- [ ] Preparar roteiro de apresentação com fallbacks (túnel → hotspot → vídeo)
- [ ] Gravar vídeo de backup funcionando em casa (2-3 reconhecimentos, mostrando o serial em tempo real)
- [ ] Verificar catálogo final: `shazamzin-ingest stats` + `verify`
- [ ] Teste de estresse: 50 requests em sequência pra garantir que não vaza memória nem no ESP nem no servidor

### Fase 7 — Polimento (opcional, semana 11+)

- [ ] OLED mostrando resultado da última música identificada
- [ ] LED de status (IDLE=azul, CAPTURING=amarelo piscando, MATCH=verde, NO_MATCH=vermelho)
- [ ] WebSocket em vez de HTTP para latência menor
- [ ] Case 3D impresso
- [ ] README do projeto polido pra portfólio/currículo
- [ ] Dashboard web simples (ASP.NET Razor Pages ou Blazor) mostrando match log e estatísticas do catálogo, reaproveitando `match_log` do SQLite

---

## 9. Dependências

### 9.1 Firmware (ESP-IDF + PlatformIO)

```ini
[env:esp32dev]
platform = espressif32
board = esp32dev
framework = espidf
monitor_speed = 115200
build_flags =
    -DCONFIG_LOG_DEFAULT_LEVEL=3
```

APIs ESP-IDF usadas:
- `esp_wifi` — conexão Wi-Fi
- `esp_http_client` — cliente HTTP (suporta HTTPS)
- `driver/i2s_std` — nova API I2S (ESP-IDF 5.x)
- `freertos/FreeRTOS.h`, `freertos/task.h`, `freertos/queue.h`
- `cJSON` — parsing da resposta
- `esp_log` — logging
- `nvs_flash` — persistência de config se precisar

Componentes externos:
- **Olaf** — como componente em `components/olaf/`

### 9.2 Backend (NuGet)

**Shazamzin.Api:**
- `Microsoft.AspNetCore.OpenApi` (10.0.0)
- `Serilog.AspNetCore`
- `Serilog.Sinks.Console`
- `Serilog.Sinks.File`

**Shazamzin.Infrastructure:**
- `StackExchange.Redis` (funciona com Valkey)
- `Microsoft.Data.Sqlite`
- `Dapper`

**Shazamzin.Ingestion:**
- `z440.atl.core` (metadados áudio)
- `System.CommandLine` (CLI)
- `Spectre.Console` (barra de progresso bonita)
- Referência a `Shazamzin.Infrastructure`

**Shazamzin.Tests:**
- `xunit`
- `FluentAssertions`
- `Microsoft.NET.Test.Sdk`

### 9.3 Ferramentas externas

- `ffmpeg` no PATH (ingestão converte áudio pra WAV mono 8kHz)
- Olaf clonado + compilado como DLL via CMake
- Docker Desktop ou Podman Desktop (Valkey)
- `cloudflared` (túnel)

---

## 10. Configuração sensível

### 10.1 `firmware/main/config.h` (gitignored, versão `.example` commitada)

```c
#pragma once

#define WIFI_SSID        "sua-rede"
#define WIFI_PASSWORD    "sua-senha"
#define SERVER_URL       "https://xxx.trycloudflare.com/match"
#define API_KEY          "dev-key-local"
#define DEVICE_ID        "esp32-01"

#define AUDIO_SAMPLE_RATE  8000
#define FFT_SIZE           1024
#define BATCH_WINDOW_MS    3000
```

### 10.2 `backend/src/Shazamzin.Api/appsettings.json`

```json
{
  "Valkey": { "ConnectionString": "localhost:6379" },
  "Sqlite": { "ConnectionString": "Data Source=./data/shazamzin.db" },
  "Auth": { "ApiKey": "dev-key-local" },
  "Matching": {
    "DeltaBucketMs": 50,
    "MinScoreThreshold": 8
  },
  "Serilog": { "MinimumLevel": "Information" }
}
```

### 10.3 `.gitignore` (highlights)

```
# Firmware
firmware/.pio/
firmware/build/
firmware/main/config.h
firmware/sdkconfig
firmware/sdkconfig.old

# Backend
backend/**/bin/
backend/**/obj/
backend/**/appsettings.*.json
backend/data/*.db

# Native
native/build/
native/*.dll
native/*.so

# Credenciais
*.secret
.env
```

---

## 11. Riscos e mitigações

| Risco | Mitigação |
|---|---|
| Fingerprints do ESP não batem com os da ingestão | Usar mesma lib C (Olaf) dos dois lados, compilada do mesmo commit; teste automatizado comparando hashes do mesmo áudio |
| INMP441 dá áudio com ruído/estalos | Validar com UDP + ffplay antes de encostar no fingerprinting (Fase 4 começa com isso) |
| Wi-Fi da faculdade bloqueia o túnel | Fallback com hotspot do celular; vídeo de backup gravado |
| ESP32 clássico fica apertado de RAM | Monitorar uso com `esp_get_free_heap_size()`; migrar para ESP32-S3 com PSRAM se necessário |
| Curva do ESP-IDF | Fase 0 dedica tempo ao tutorial oficial; background em C/C++ reduz atrito |
| Olaf tem patentes US | Projeto pessoal/acadêmico tá coberto; não comercializar |
| Ingestão de 5k+ músicas falha no meio | Pipeline idempotente e resumível por design (seção 6.1) |
| Catálogo passa de 50k e estoura RAM do Valkey | Monitoring via `shazamzin-ingest stats`; otimização de encoding só se precisar |
| Latência fim-a-fim grande demais pra demo | Reduzir janela de captura ou migrar pra WebSocket na Fase 7 |
| Notebook dorme durante apresentação | Configurar pra não dormir conectado + manter plug durante demo |

---

## 12. Como iniciar com Claude Code

Recomendação de abordagem ao abrir o Claude Code:

1. **Primeira sessão:** crie o monorepo com a estrutura de pastas da seção 7, inicialize git, faça o setup da Fase 0 completa (ambiente, "hello world" dos dois lados, Valkey rodando, Olaf compilado standalone)
2. **Sessões subsequentes:** uma fase por vez, com commits atômicos ao final de cada entregável marcado
3. **Peça para o Claude Code** sempre ler `docs/architecture.md` e `docs/sprint-log.md` ao iniciar sessão nova — isso evita re-explicar contexto toda vez
4. **Atualize `docs/sprint-log.md`** ao final de cada sessão com: o que ficou pronto, decisões tomadas, o que falta, bugs conhecidos
5. **Teste antes de avançar de fase:** nenhuma fase deveria ser dada por encerrada sem o entregável dela funcionando ponta a ponta
6. **Use branches por fase** (`feature/fase-1-backend-fake`, etc.) e merge na main ao concluir cada uma

**Prompt inicial sugerido para o Claude Code:**

> Vou trabalhar no projeto Shazam-ESP32. O plano completo está em `docs/architecture.md`. Estamos iniciando a Fase 0 (setup e familiarização). Leia o plano, confirme que entendeu a arquitetura, e me ajude a montar a estrutura do monorepo conforme a seção 7 e completar todos os itens da Fase 0. Como é minha primeira experiência com ESP-IDF, vá devagar na parte do firmware e me explique o que cada passo faz.

---

## 13. Referências

- **Olaf (lib principal):** https://github.com/JorenSix/Olaf
- **Paper original do Shazam:** Avery Li-Chun Wang, "An Industrial-Strength Audio Search Algorithm" (2003)
- **ESP-IDF Getting Started:** https://docs.espressif.com/projects/esp-idf/en/latest/esp32/get-started/
- **ESP-IDF I2S Driver (nova API):** https://docs.espressif.com/projects/esp-idf/en/latest/esp32/api-reference/peripherals/i2s.html
- **ESP-DSP (FFT acelerada, opcional):** https://github.com/espressif/esp-dsp
- **Valkey:** https://valkey.io/
- **StackExchange.Redis docs:** https://stackexchange.github.io/StackExchange.Redis/
- **Cloudflare Tunnel docs:** https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/
- **.NET 10 novidades:** https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/

---

**Este documento é a fonte da verdade para a arquitetura do projeto.** Atualize conforme decisões forem tomadas; versione junto com o código em `docs/architecture.md`.
