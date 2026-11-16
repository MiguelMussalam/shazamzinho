#include "protocolo.h"
#include <string.h>

// Little-endian, campo a campo (ver docs/protocol.md). TODO Fase 5: testar vs C#.

static void escrever_u16le(uint8_t *b, uint16_t v)
{
    b[0] = v & 0xFF;
    b[1] = (v >> 8) & 0xFF;
}

static void escrever_u32le(uint8_t *b, uint32_t v)
{
    b[0] = v & 0xFF;
    b[1] = (v >> 8) & 0xFF;
    b[2] = (v >> 16) & 0xFF;
    b[3] = (v >> 24) & 0xFF;
}

size_t protocolo_codificar(uint32_t id_sessao, const fp_entry_t *entradas, uint16_t qtd,
                           uint8_t *buf, size_t cap)
{
    size_t necessario = PROTOCOLO_BYTES_CABECALHO + (size_t) qtd * PROTOCOLO_BYTES_ENTRADA;
    if (cap < necessario) return 0;

    // Cabeçalho: versão, reservado, quantidade, id da sessão.
    buf[0] = PROTOCOLO_VERSAO;
    buf[1] = 0; // reservado
    escrever_u16le(buf + 2, qtd);
    escrever_u32le(buf + 4, id_sessao);

    // Entradas: hash + timestamp (ms) de cada fingerprint.
    for (uint16_t i = 0; i < qtd; i++) {
        uint8_t *e = buf + PROTOCOLO_BYTES_CABECALHO + (size_t) i * PROTOCOLO_BYTES_ENTRADA;
        escrever_u32le(e,     entradas[i].hash);
        escrever_u32le(e + 4, entradas[i].timestamp_ms);
    }
    return necessario;
}
