# Notas de ESP-IDF

Dicas específicas de ESP-IDF para reusar depois. Atualize conforme aprender coisas
durante as Fases 4–6.

## Instalação (pendente na Fase 0)

Opção escolhida: **PlatformIO** com `framework = espidf` (ver `firmware/platformio.ini`).

1. Instalar a extensão PlatformIO IDE no VS Code
2. Abrir a pasta `firmware/` — o PlatformIO baixa o toolchain ESP-IDF 5.4 na primeira build
3. Alternativa: extensão oficial "Espressif IDF" no VS Code

> Status: toolchain ainda não instalada nesta máquina. `pio` não está no PATH.

## Build / flash / monitor (PlatformIO CLI)

```bash
pio run                 # compila
pio run -t upload       # grava na placa
pio device monitor      # monitor serial (115200)
pio run -t upload -t monitor
```

## Pinagem INMP441 (seção 4.3 do architecture.md)

| INMP441 | ESP32    | Função       |
|---------|----------|--------------|
| VDD     | 3.3V     | Alimentação  |
| GND     | GND      | Terra        |
| L/R     | GND      | Canal esq.   |
| WS      | GPIO 15  | Word Select  |
| SCK     | GPIO 14  | Bit Clock    |
| SD      | GPIO 32  | Data out     |

O GPIO 15 é um pino de strapping do ESP32 com pull-up interno. O `WS` do INMP441
é uma entrada. Confira o boot com o módulo conectado. O botão BOOT usa GPIO 0.
[Datasheet ESP32](https://documentation.espressif.com/esp32_datasheet_en.html)

## I2S (nova API ESP-IDF 5.x)

Usar `driver/i2s_std.h` (`i2s_std_new_channel`, não a API legada `i2s_driver_install`).
INMP441 é um mic I2S de 24 bits em slot de 32 bits; ler como `I2S_DATA_BIT_WIDTH_32BIT`
e deslocar. Sample rate alvo: 8000 Hz mono (seção 10.1).

## Checklist de armadilhas

- [ ] Monitorar heap com `esp_get_free_heap_size()` — ESP32 clássico é apertado
- [ ] `config.h` é gitignored; commitar só `config.h.example`
- [ ] Core 0 = Wi-Fi/TCP/IP; Core 1 = app tasks (ver seção 3.3)
- [ ] Validar áudio via UDP + `ffplay` **antes** de ligar no fingerprinting (Fase 4)
- [ ] Hashes do ESP devem bater bit-a-bit com os da ingestão (mesmo commit do Olaf)

## Referências

- Getting Started: https://docs.espressif.com/projects/esp-idf/en/latest/esp32/get-started/
- I2S driver: https://docs.espressif.com/projects/esp-idf/en/latest/esp32/api-reference/peripherals/i2s.html
- Olaf: https://github.com/JorenSix/Olaf
