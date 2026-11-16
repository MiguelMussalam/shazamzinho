#pragma once
// Cliente HTTP (esp_http_client) para POST /match. Implementação na Fase 5.
#include <stdint.h>
#include <stddef.h>

void network_init(void);
// Envia payload binário (header+entries) e preenche 'resp_json'. Retorna 0 em sucesso.
int network_post_match(const uint8_t *payload, size_t len, char *resp_json, size_t resp_cap);
