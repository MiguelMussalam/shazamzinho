# Firmware ESP32 — Shazamzin

ESP-IDF 5.4 via PlatformIO, C puro. Ver `../docs/architecture.md` (seções 3.3, 4, 9.1)
e `../docs/esp-idf-notes.md`.

## Antes de compilar

```bash
cp main/config.h.example main/config.h   # preencha SSID/senha/URL/API key
```

`config.h` é gitignored.

## Build / flash / monitor

Requer a toolchain ESP-IDF (PlatformIO baixa na primeira build):

```bash
pio run                      # compila
pio run -t upload -t monitor # grava e abre o serial (115200)
```

## Estado

- **Fase 0 (feito):** `main.c` conecta no Wi-Fi e imprime o IP no serial.
- **Fases 4-5 (stubs):** `audio_capture`, `fingerprint`, `network`, `protocol`,
  `state_machine` — headers prontos, corpos com TODO marcando a fase.
- **Olaf:** entra em `components/olaf/` na Fase 4.
