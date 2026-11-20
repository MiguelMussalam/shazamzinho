#pragma once
// Máquina de estados: OCIOSO -> CAPTURANDO -> PROCESSANDO -> CONSULTANDO -> RESULTADO -> OCIOSO.
// Disparada pelo botão BOOT (GPIO 0) e uma vez automaticamente no boot.
#include <stdbool.h>

typedef enum {
    ESTADO_OCIOSO,
    ESTADO_CAPTURANDO,
    ESTADO_PROCESSANDO,
    ESTADO_CONSULTANDO,
    ESTADO_RESULTADO,
} estado_t;

// 'ip' é mostrado na tela (pode ser NULL). Sem Wi-Fi, o ciclo para depois do
// fingerprint (útil para comparar os hashes com o PC sem rede).
void maquina_estado_iniciar(const char *ip, bool wifi_ok);
