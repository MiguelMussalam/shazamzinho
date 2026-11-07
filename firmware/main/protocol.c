#include "protocol.h"
#include <string.h>
// Little-endian, campo a campo (ver docs/protocol.md). TODO Fase 5: testar vs C#.
static void put_u16le(uint8_t *b, uint16_t v) { b[0]=v&0xFF; b[1]=(v>>8)&0xFF; }
static void put_u32le(uint8_t *b, uint32_t v) { b[0]=v&0xFF; b[1]=(v>>8)&0xFF; b[2]=(v>>16)&0xFF; b[3]=(v>>24)&0xFF; }

size_t protocol_encode(uint32_t session_id, const fp_entry_t *entries, uint16_t count,
                       uint8_t *buf, size_t cap) {
    size_t need = PROTOCOL_HEADER_BYTES + (size_t)count * PROTOCOL_ENTRY_BYTES;
    if (cap < need) return 0;
    buf[0] = PROTOCOL_VERSION;
    buf[1] = 0; // reserved
    put_u16le(buf + 2, count);
    put_u32le(buf + 4, session_id);
    for (uint16_t i = 0; i < count; i++) {
        uint8_t *e = buf + PROTOCOL_HEADER_BYTES + (size_t)i * PROTOCOL_ENTRY_BYTES;
        put_u32le(e,     entries[i].hash);
        put_u32le(e + 4, entries[i].timestamp_ms);
    }
    return need;
}
