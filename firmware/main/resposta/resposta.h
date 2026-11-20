#pragma once
// Leitura da resposta JSON do POST /match (docs/architecture.md §5.2).
#include <stdbool.h>

typedef struct {
    bool match;
    int song_id;
    char titulo[96];
    char artista[96];
    double confianca;       // 0..1
    double offset_s;        // posição na música onde a captura começou
    int tempo_servidor_ms;  // processing_time_ms
    int hashes_recebidos;   // só no no-match
    int hashes_acertados;   // só no no-match
} resposta_t;

// Preenche 'r' a partir do JSON. Retorna false se o JSON for inválido.
bool resposta_ler(const char *json, resposta_t *r);
