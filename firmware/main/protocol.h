#pragma once
// Serialização binária do payload (ver docs/protocol.md). Implementação na Fase 5.
#include <stdint.h>
#include <stddef.h>
#include "fingerprint.h"

#define PROTOCOL_VERSION 1
#define PROTOCOL_HEADER_BYTES 8
#define PROTOCOL_ENTRY_BYTES  8

// Serializa 'count' entries + header em 'buf'. Retorna bytes escritos, ou 0 se não coube.
size_t protocol_encode(uint32_t session_id, const fp_entry_t *entries, uint16_t count,
                       uint8_t *buf, size_t cap);
