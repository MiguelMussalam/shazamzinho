#pragma once
// Fingerprinting via lib Olaf. Implementação na Fase 4.
#include <stdint.h>
#include <stddef.h>

typedef struct {
    uint32_t hash;
    uint32_t timestamp_ms;
} fp_entry_t;

void fingerprint_init(void);
// Processa amostras e preenche 'out' (até max). Retorna nº de fingerprints.
size_t fingerprint_process(const int16_t *samples, size_t n, fp_entry_t *out, size_t max);
