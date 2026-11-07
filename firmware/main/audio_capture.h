#pragma once
// Captura de áudio I2S (INMP441). Implementação na Fase 4.
// Ver docs/architecture.md seções 3.3 e 4.3, docs/esp-idf-notes.md.
#include <stdint.h>
#include <stddef.h>

// Inicializa o canal I2S std (GPIO WS=15, SCK=14, SD=32) a AUDIO_SAMPLE_RATE.
void audio_capture_init(void);

// Lê 'max_samples' amostras mono int16 para 'out'. Retorna quantas leu.
size_t audio_capture_read(int16_t *out, size_t max_samples);
