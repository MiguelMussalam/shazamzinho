#pragma once
// Captura de áudio I2S (INMP441), 8000 Hz mono, canal esquerdo (L/R no GND).
// Ver docs/architecture.md seções 3.3 e 4.3, docs/esp-idf-notes.md.
#include <stdint.h>
#include <stddef.h>

// Estatísticas de nível de uma captura, para ajustar o ganho olhando o serial.
typedef struct {
    size_t amostras;
    size_t saturadas;   // amostras que bateram em ±1.0 depois do ganho
    float pico;         // maior |amostra| depois do ganho (0..1)
    double soma_quad;   // para o RMS
} captura_estat_t;

// Inicializa o canal I2S std (GPIO WS=15, SCK=14, SD=32) a 8000 Hz.
void captura_audio_iniciar(void);

// Lê até 'max' amostras como float normalizado (-1..1), multiplicadas por 'ganho' e saturadas.
// O INMP441 entrega 24 bits num slot de 32; o microfone capta bem mais baixo que um MP3
// decodificado, por isso o ganho (o exemplo ESP32 do Olaf usa 40x). Atualiza 'est' se não for NULL.
size_t captura_audio_ler_float(float *saida, size_t max, float ganho, captura_estat_t *est);

// Versão int16 (com saturação), usada pela barra de VU.
size_t captura_audio_ler(int16_t *saida, size_t max_amostras);

// Converte as estatísticas em dBFS (pico e RMS) e % saturado.
void captura_estat_resumo(const captura_estat_t *est, float *pico_dbfs, float *rms_dbfs, float *pct_saturado);
