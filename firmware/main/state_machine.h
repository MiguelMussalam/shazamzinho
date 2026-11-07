#pragma once
// Máquina de estados: IDLE -> CAPTURING -> PROCESSING -> QUERYING -> RESULT -> IDLE.
// Implementação na Fase 5 (trigger pelo botão BOOT, GPIO 0).
typedef enum { ST_IDLE, ST_CAPTURING, ST_PROCESSING, ST_QUERYING, ST_RESULT } sz_state_t;
void state_machine_start(void);
