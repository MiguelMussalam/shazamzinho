#include "maquina_de_estado.h"
#include <stdio.h>
#include <string.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_system.h"
#include "esp_log.h"
#include "captura_de_audio.h"
#include "display.h"

// Fase 4 — "sinal de vida": uma task fixada no core 1 lê o microfone, calcula o
// nível de áudio e desenha status + IP + barra de VU no OLED. Ainda sem Olaf/rede
// (ver fingerprint.c / network.c). Orquestração completa da Fase 5 entra depois.

#define AMOSTRAS_POR_BLOCO 256
#define VU_PASSO_QUEDA     6   // a barra desce ~6%/bloco (decaimento suave p/ não piscar)

static const char *TAG = "maquina";
static char s_ip[32] = "sem IP";

// Converte um bloco de amostras no nível 0..100 (baseado no pico).
static int nivel_do_bloco(const int16_t *buf, size_t qtd)
{
    int32_t pico = 0;
    for (size_t i = 0; i < qtd; i++) {
        int32_t amplitude = buf[i] < 0 ? -(int32_t) buf[i] : buf[i];
        if (amplitude > pico) pico = amplitude;
    }
    // Pico 0..32767 -> 0..100.
    int nivel = (int) ((pico * 100) / 32767);
    return nivel > 100 ? 100 : nivel;
}

static void tarefa_sinal_de_vida(void *arg)
{
    (void) arg;

    display_init();
    captura_audio_iniciar();

    display_clear();
    display_text(0, 0, "Shazamzin");
    display_text(0, 2, "Iniciando...");
    display_flush();

    int16_t buf[AMOSTRAS_POR_BLOCO];
    int exibido = 0;   // nível suavizado mostrado na barra

    while (1) {
        size_t qtd = captura_audio_ler(buf, AMOSTRAS_POR_BLOCO);
        int nivel = qtd ? nivel_do_bloco(buf, qtd) : 0;

        // Decaimento suave: sobe na hora, desce aos poucos.
        if (nivel >= exibido) {
            exibido = nivel;
        } else {
            exibido -= VU_PASSO_QUEDA;
            if (exibido < nivel) exibido = nivel;
        }
        if (exibido < 0) exibido = 0;

        display_clear();
        display_text(0, 0, "Shazamzin");
        display_text(0, 2, s_ip);
        display_text(0, 4, "Ouvindo...");
        display_vu(7, exibido);
        display_flush();

        // Log periódico p/ depurar sem olhar a tela.
        static int contador = 0;
        if (++contador % 20 == 0) {
            ESP_LOGI(TAG, "nivel=%d (heap=%lu)",
                     exibido, (unsigned long) esp_get_free_heap_size());
        }

        vTaskDelay(pdMS_TO_TICKS(40));
    }
}

void maquina_estado_iniciar(const char *ip)
{
    if (ip) {
        snprintf(s_ip, sizeof(s_ip), "%s", ip);
    }
    // Core 1: o core 0 fica com Wi-Fi/TCP-IP (ver docs/architecture.md 3.3).
    xTaskCreatePinnedToCore(tarefa_sinal_de_vida, "sinal_vida", 4096, NULL, 5, NULL, 1);
    ESP_LOGI(TAG, "task de sinal de vida iniciada (IP=%s)", s_ip);
}
