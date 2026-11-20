#pragma once
// Fingerprinting com o Olaf (componente components/olaf), mesmo código e perfil
// da libolaf.dll usada na ingestão do catálogo no PC.
#include <stdint.h>
#include <stddef.h>

typedef struct {
    uint32_t hash;
    uint32_t timestamp_ms;
} fp_entry_t;

// Loga o perfil do Olaf (deve ser igual ao "profile" impresso pela CLI de ingestão).
void fingerprint_init(void);
const char *fingerprint_perfil(void);

// Extrai fingerprints de PCM mono float32 normalizado (-1..1) a 8 kHz.
// Guarda até 'max' em 'out' e retorna quantos guardou. Se houver mais que 'max',
// o excedente é contado em *descartados (pode ser NULL). Retorna 0 em erro do Olaf.
size_t fingerprint_extrair_float(const float *pcm, size_t n, fp_entry_t *out, size_t max,
                                 size_t *descartados);

// Mesmo que o anterior, para amostras int16 do microfone (convertidas para float).
size_t fingerprint_process(const int16_t *samples, size_t n, fp_entry_t *out, size_t max);
