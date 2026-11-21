#pragma once
// Cliente HTTP(S) para o POST /match (docs/protocol.md).
#include <stdint.h>
#include <stddef.h>

void network_init(void);

// Envia o payload binário para SERVER_URL com X-Api-Key e X-Device-ID.
// Copia o corpo da resposta (terminado em '\0', truncado em resp_cap-1) para resp_json.
// Retorna o status HTTP (200, 400, 401...) ou -1 em falha de conexão/TLS/timeout.
int network_post_match(const uint8_t *payload, size_t len, char *resp_json, size_t resp_cap);
