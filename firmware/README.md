# Firmware ESP32 — Shazamzin

ESP-IDF 5.4 via PlatformIO, C puro. Ver `../docs/architecture.md` (seções 3.3, 4, 9.1)
e `../docs/esp-idf-notes.md`.

## Antes de compilar

```bash
../native/build.ps1                      # clona o Olaf no commit fixado (o componente usa esses fontes)
cp main/config.h.example main/config.h   # preencha SSID/senha/URL/API key
```

`config.h` é gitignored. `SERVER_URL` e `API_KEY` saem prontos da saída de
`../scripts/subir-servidor.ps1`. O Wi-Fi precisa ser de 2,4 GHz.

## Build / flash / monitor

Requer a toolchain ESP-IDF (PlatformIO baixa na primeira build):

```bash
pio run                      # compila
pio run -t upload -t monitor # grava e abre o serial (115200)
```

## Fluxo

`OCIOSO → (CAPTURANDO) → PROCESSANDO → CONSULTANDO → RESULTADO → OCIOSO`, uma vez
automaticamente no boot e depois a cada clique no botão **BOOT** (GPIO 0, já vem na placa).

| Módulo | Função |
|---|---|
| `captura_de_audio/` | INMP441 via I²S (`i2s_std`), 8 kHz mono |
| `clipe/` | trecho de música na flash, usado quando `FONTE_AUDIO_CLIPE = 1` (sem microfone) |
| `fingerprint/` | Olaf (`components/olaf`), mesmo código e perfil do catálogo |
| `protocolo/` | payload binário v1 (`docs/protocol.md`) |
| `network/` | `POST /match` via `esp_http_client`, HTTPS com o bundle de certificados do ESP-IDF |
| `resposta/` | leitura do JSON com cJSON |
| `display/` | OLED SSD1306 opcional: sem ele o firmware segue só com o serial |
| `maquina_de_estado/` | orquestra o ciclo; imprime os fingerprints e o `RESULTADO` no serial |

## Estado (2026-11-20)

Validado numa placa **sem microfone e sem OLED**, com `FONTE_AUDIO_CLIPE = 1`, no hotspot do
celular, consultando a API pelo túnel do Cloudflare:

- fingerprints do ESP **100% iguais** aos do PC no mesmo PCM (119/119), em ~720 ms;
- música reconhecida corretamente em **~3,6 s** do início ao resultado
  (Olaf 0,7 s + HTTPS ~2,7 s + servidor < 10 ms);
- heap volta a ~214 KB livres após cada ciclo; pior momento ~70 KB livres (durante o Olaf).

**Captura em fluxo:** com o microfone, cada bloco de 256 amostras vai direto para o Olaf
enquanto grava (5 s em float seriam 160 KB, mais que o maior bloco livre). A API de fluxo da
ponte nativa dá resultado idêntico à extração do PC (`OlafStreamTests`), e o modo clipe também
passa por ela: 119/119 iguais na placa. O modo microfone foi rodado **sem o microfone ligado**:
captura de 5,0 s, silêncio detectado, consulta "não reconhecida", heap mínimo 55 KB, sem travar.

Ganho do microfone em `GANHO_MIC` (padrão 40x, como o exemplo ESP32 do Olaf), com saturação.
Cada captura loga pico/RMS em dBFS e % saturado para ajustar o ganho.

Falta validar com o microfone e o OLED físicos: roteiro em `../docs/teste-integrado.md`.

O OLED usa o driver SSD1306 que já vem no componente `esp_lcd` do ESP-IDF 5.4
(sem dependências externas); o `audio_capture` usa a API `i2s_std`.
