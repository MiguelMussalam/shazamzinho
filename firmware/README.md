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
- **Sinal de vida (feito):** `audio_capture` (I2S/INMP441), `display` (OLED SSD1306)
  e `state_machine` lêem o microfone e mostram status + IP + barra de VU no OLED.
  A barra se mexe conforme o som captado — prova mic + display + CPU juntos.
- **Fases 4-5 (stubs):** `fingerprint`, `network` — ainda com TODO. `protocol` está
  implementado mas não validado contra o servidor.
- **Olaf:** entra em `components/olaf/` na Fase 4 (fingerprint real).

O OLED usa o driver SSD1306 que já vem no componente `esp_lcd` do ESP-IDF 5.4
(sem dependências externas); o `audio_capture` usa a API `i2s_std`.
