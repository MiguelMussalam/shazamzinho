#pragma once
// Máquina de estados: OCIOSO -> CAPTURANDO -> PROCESSANDO -> CONSULTANDO -> RESULTADO.
// Fluxo completo na Fase 5 (trigger pelo botão BOOT, GPIO 0).
typedef enum {
    ESTADO_OCIOSO,
    ESTADO_CAPTURANDO,
    ESTADO_PROCESSANDO,
    ESTADO_CONSULTANDO,
    ESTADO_RESULTADO,
} estado_t;

// Fase 4 (sinal de vida): inicia a task que lê o microfone e atualiza o OLED
// (status + IP + barra de VU). 'ip' é mostrado na tela; pode ser NULL.
void maquina_estado_iniciar(const char *ip);
