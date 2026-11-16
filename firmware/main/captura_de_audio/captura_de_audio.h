#pragma once
// Captura de áudio I2S (INMP441). Implementação na Fase 4.
// Ver docs/architecture.md seções 3.3 e 4.3, docs/esp-idf-notes.md.
#include <stdint.h>
#include <stddef.h>

// Inicializa o canal I2S std (GPIO WS=15, SCK=14, SD=32) a 8000 Hz.
void captura_audio_iniciar(void);

// Lê até 'max_amostras' amostras mono int16 para 'saida'. Retorna quantas leu.
size_t captura_audio_ler(int16_t *saida, size_t max_amostras);
