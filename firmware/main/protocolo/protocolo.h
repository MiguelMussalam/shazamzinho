#pragma once
// Serialização binária do payload (ver docs/protocol.md). Implementação na Fase 5.
#include <stdint.h>
#include <stddef.h>
#include "fingerprint.h"

#define PROTOCOLO_VERSAO          1
#define PROTOCOLO_BYTES_CABECALHO 8
#define PROTOCOLO_BYTES_ENTRADA   8

// Serializa 'qtd' entradas + cabeçalho em 'buf'. Retorna bytes escritos, ou 0 se não coube.
// (fp_entry_t vem do módulo fingerprint.)
size_t protocolo_codificar(uint32_t id_sessao, const fp_entry_t *entradas, uint16_t qtd,
                           uint8_t *buf, size_t cap);
