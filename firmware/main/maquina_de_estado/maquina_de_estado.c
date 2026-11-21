#include "maquina_de_estado.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "driver/gpio.h"
#include "esp_heap_caps.h"
#include "esp_log.h"
#include "esp_random.h"
#include "esp_timer.h"
#include "config.h"
#include "captura_de_audio.h"
#include "clipe_teste.h"
#include "display.h"
#include "fingerprint.h"
#include "network.h"
#include "protocolo.h"
#include "resposta.h"

// FONTE_AUDIO_CLIPE (config.h): 1 = trecho gravado na flash, 0 = microfone INMP441.
#ifndef FONTE_AUDIO_CLIPE
#define FONTE_AUDIO_CLIPE 0
#endif
// Ganho aplicado ao microfone (config.h). O exemplo ESP32 do Olaf usa 40x.
#ifndef GANHO_MIC
#define GANHO_MIC 40.0f
#endif

#define BOTAO_BOOT_GPIO     GPIO_NUM_0
#define FP_MAX              1024                       // limite de fingerprints por consulta
#define CAPTURA_MS          BATCH_WINDOW_MS            // duração da captura do microfone
#define AMOSTRAS_POR_BLOCO  256
#define VU_PASSO_QUEDA      6
#define RESPOSTA_MAX        768

static const char *TAG = "maquina";
static char s_ip[32] = "sem IP";
static bool s_wifi_ok = false;
static estado_t s_estado = ESTADO_OCIOSO;

static const char *nome_estado(estado_t e)
{
    switch (e) {
    case ESTADO_OCIOSO: return "OCIOSO";
    case ESTADO_CAPTURANDO: return "CAPTURANDO";
    case ESTADO_PROCESSANDO: return "PROCESSANDO";
    case ESTADO_CONSULTANDO: return "CONSULTANDO";
    case ESTADO_RESULTADO: return "RESULTADO";
    }
    return "?";
}

static void mudar_estado(estado_t e)
{
    s_estado = e;
    ESP_LOGI(TAG, "estado -> %s", nome_estado(e));
}

// ---------- tela (no-op sem OLED) ----------

static void tela(const char *l2, const char *l4, const char *l5, const char *l6)
{
    display_clear();
    display_text(0, 0, "Shazamzin");
    if (l2) display_text(0, 2, l2);
    if (l4) display_text(0, 4, l4);
    if (l5) display_text(0, 5, l5);
    if (l6) display_text(0, 6, l6);
    display_flush();
}

// ---------- botão BOOT ----------

static void botao_iniciar(void)
{
    gpio_config_t cfg = {
        .pin_bit_mask = 1ULL << BOTAO_BOOT_GPIO,
        .mode = GPIO_MODE_INPUT,
        .pull_up_en = GPIO_PULLUP_ENABLE,
    };
    gpio_config(&cfg);
}

// true quando o botão foi pressionado e solto (com debounce). Não bloqueia se solto.
static bool botao_clicado(void)
{
    if (gpio_get_level(BOTAO_BOOT_GPIO) != 0) return false;
    vTaskDelay(pdMS_TO_TICKS(30));
    if (gpio_get_level(BOTAO_BOOT_GPIO) != 0) return false;
    while (gpio_get_level(BOTAO_BOOT_GPIO) == 0) vTaskDelay(pdMS_TO_TICKS(10));
    return true;
}

// ---------- nível do microfone (sinal de vida) ----------

#if !FONTE_AUDIO_CLIPE
static int nivel_do_bloco(const int16_t *buf, size_t qtd)
{
    int32_t pico = 0;
    for (size_t i = 0; i < qtd; i++) {
        int32_t a = buf[i] < 0 ? -(int32_t) buf[i] : buf[i];
        if (a > pico) pico = a;
    }
    int nivel = (int) ((pico * 100) / 32767);
    return nivel > 100 ? 100 : nivel;
}

static void ocioso_vu(int *exibido)
{
    int16_t buf[AMOSTRAS_POR_BLOCO];
    size_t qtd = captura_audio_ler(buf, AMOSTRAS_POR_BLOCO);
    int nivel = qtd ? nivel_do_bloco(buf, qtd) : 0;
    if (nivel >= *exibido) *exibido = nivel;
    else if ((*exibido -= VU_PASSO_QUEDA) < nivel) *exibido = nivel;

    display_clear();
    display_text(0, 0, "Shazamzin");
    display_text(0, 2, s_ip);
    display_text(0, 4, "BOOT = reconhecer");
    display_vu(7, *exibido);
    display_flush();
}
#endif

// ---------- um ciclo de reconhecimento ----------

static void log_heap(const char *quando)
{
    ESP_LOGI(TAG, "heap %s: livre=%u minimo_historico=%u maior_bloco=%u", quando,
             (unsigned) esp_get_free_heap_size(), (unsigned) esp_get_minimum_free_heap_size(),
             (unsigned) heap_caps_get_largest_free_block(MALLOC_CAP_8BIT));
}

// Imprime os hashes num formato que scripts/comparar-fingerprints-esp.ps1 lê do serial.
static void despejar_fingerprints(const fp_entry_t *fps, size_t qtd)
{
    printf("FP_INICIO n=%u perfil=%s\n", (unsigned) qtd, fingerprint_perfil());
    for (size_t i = 0; i < qtd; i++) {
        printf("FP %08lx %lu\n", (unsigned long) fps[i].hash, (unsigned long) fps[i].timestamp_ms);
    }
    printf("FP_FIM\n");
    fflush(stdout);
}

static void ciclo_reconhecimento(void)
{
    const int64_t t_inicio = esp_timer_get_time();
    log_heap("antes");

    fp_entry_t *fps = malloc(FP_MAX * sizeof(fp_entry_t));
    uint8_t *payload = malloc(PROTOCOLO_BYTES_CABECALHO + FP_MAX * PROTOCOLO_BYTES_ENTRADA);
    char *json = malloc(RESPOSTA_MAX);
    if (!fps || !payload || !json) {
        ESP_LOGE(TAG, "sem memoria para o ciclo");
        tela("Erro:", "sem memoria", NULL, NULL);
        goto fim;
    }

    size_t qtd = 0;
#if FONTE_AUDIO_CLIPE
    ESP_LOGI(TAG, "fonte: clipe na flash (%u amostras, %.1f s) — %s", (unsigned) clipe_teste_amostras,
             clipe_teste_amostras / (double) AUDIO_SAMPLE_RATE, clipe_teste_origem);
    mudar_estado(ESTADO_PROCESSANDO);
    tela("Processando", "clipe de teste", NULL, NULL);
    const int64_t t_fp = esp_timer_get_time();
    size_t descartados = 0;
    qtd = fingerprint_extrair_float(clipe_teste, clipe_teste_amostras, fps, FP_MAX, &descartados);
#else
    // Captura e fingerprint ao mesmo tempo: cada bloco de 256 amostras (32 ms) vai direto para
    // o Olaf. Nada de buffer de 5 s (seriam 160 KB em float, mais que o maior bloco livre).
    mudar_estado(ESTADO_CAPTURANDO);
    char linha_ouvindo[24];
    snprintf(linha_ouvindo, sizeof(linha_ouvindo), "Ouvindo %d s...", CAPTURA_MS / 1000);
    tela(linha_ouvindo, NULL, NULL, NULL);
    const int64_t t_fp = esp_timer_get_time();
    fp_sessao_t *sessao = fingerprint_sessao_nova(fps, FP_MAX);
    if (!sessao) {
        tela("Erro:", "sem memoria", NULL, NULL);
        goto fim;
    }
    const size_t total = (size_t) AUDIO_SAMPLE_RATE * CAPTURA_MS / 1000;
    captura_estat_t est = {0};
    float bloco[256];
    size_t lidas = 0;
    while (lidas < total) {
        size_t n = captura_audio_ler_float(bloco, total - lidas < 256 ? total - lidas : 256, GANHO_MIC, &est);
        if (n == 0) {
            ESP_LOGE(TAG, "microfone nao entregou amostras (I2S): confira a ligacao");
            break;
        }
        lidas += n;
        if (!fingerprint_sessao_alimentar(sessao, bloco, n)) break;
    }
    mudar_estado(ESTADO_PROCESSANDO);
    tela("Processando", NULL, NULL, NULL);
    size_t descartados = 0;
    qtd = fingerprint_sessao_finalizar(sessao, &descartados);

    float pico_db, rms_db, pct_sat;
    captura_estat_resumo(&est, &pico_db, &rms_db, &pct_sat);
    ESP_LOGI(TAG, "captura: %.1f s, ganho %.0fx, pico %.1f dBFS, rms %.1f dBFS, saturadas %.2f%%",
             lidas / (double) AUDIO_SAMPLE_RATE, (double) GANHO_MIC, pico_db, rms_db, pct_sat);
    if (pct_sat > 1.0f) {
        ESP_LOGW(TAG, "muitas amostras saturadas: diminua GANHO_MIC no config.h ou afaste a caixa de som");
    } else if (pico_db < -30.0f) {
        ESP_LOGW(TAG, "audio muito baixo: aumente GANHO_MIC no config.h ou aproxime a caixa de som");
    }
#endif
    const int64_t ms_fp = (esp_timer_get_time() - t_fp) / 1000;
    ESP_LOGI(TAG, "Olaf: %u fingerprints em %lld ms", (unsigned) qtd, ms_fp);
    log_heap("depois do Olaf");
    despejar_fingerprints(fps, qtd);

    if (!s_wifi_ok) {
        ESP_LOGW(TAG, "sem Wi-Fi: ciclo encerrado antes da consulta");
        tela(s_ip, "Fingerprints:", "ok (sem Wi-Fi)", "ver serial");
        goto fim;
    }

    mudar_estado(ESTADO_CONSULTANDO);
    tela("Consultando", "servidor...", NULL, NULL);
    size_t bytes = protocolo_codificar(esp_random(), fps, (uint16_t) qtd, payload,
                                       PROTOCOLO_BYTES_CABECALHO + FP_MAX * PROTOCOLO_BYTES_ENTRADA);
    const int64_t t_rede = esp_timer_get_time();
    int status = network_post_match(payload, bytes, json, RESPOSTA_MAX);
    const int64_t ms_rede = (esp_timer_get_time() - t_rede) / 1000;

    mudar_estado(ESTADO_RESULTADO);
    if (status != 200) {
        ESP_LOGE(TAG, "HTTP %d em %lld ms: %s", status, ms_rede, json);
        char linha[24];
        snprintf(linha, sizeof(linha), status < 0 ? "sem conexao" : "HTTP %d", status);
        tela("Erro na consulta", linha, NULL, NULL);
        goto fim;
    }

    resposta_t r;
    if (!resposta_ler(json, &r)) {
        ESP_LOGE(TAG, "JSON invalido: %s", json);
        tela("Erro:", "resposta invalida", NULL, NULL);
        goto fim;
    }

    const long long ms_total = (esp_timer_get_time() - t_inicio) / 1000;
    if (r.match) {
        ESP_LOGI(TAG, "RESULTADO match=1 song_id=%d titulo=\"%s\" artista=\"%s\" confianca=%.2f offset=%.1fs "
                 "servidor=%dms rede=%lldms fp=%lldms total=%lldms",
                 r.song_id, r.titulo, r.artista, r.confianca, r.offset_s, r.tempo_servidor_ms, ms_rede, ms_fp, ms_total);
        char conf[24];
        snprintf(conf, sizeof(conf), "conf %.0f%% @%.0fs", r.confianca * 100, r.offset_s);
        tela("Reconhecida:", r.titulo, r.artista, conf);
    } else {
        ESP_LOGI(TAG, "RESULTADO match=0 hashes_recebidos=%d hashes_acertados=%d rede=%lldms total=%lldms",
                 r.hashes_recebidos, r.hashes_acertados, ms_rede, ms_total);
        tela("Nao reconhecida", NULL, NULL, NULL);
    }

fim:
    free(fps);
    free(payload);
    free(json);
    log_heap("depois do ciclo");
    mudar_estado(ESTADO_OCIOSO);
}

static void tarefa_principal(void *arg)
{
    (void) arg;
    display_init();
    botao_iniciar();
    fingerprint_init();
    network_init();
#if !FONTE_AUDIO_CLIPE
    captura_audio_iniciar();
#endif

    // Um ciclo automático no boot: um reset já testa o caminho inteiro.
    ciclo_reconhecimento();
    ESP_LOGI(TAG, "aperte BOOT para reconhecer de novo");

#if !FONTE_AUDIO_CLIPE
    int exibido = 0;
#endif
    while (1) {
        if (botao_clicado()) {
            ciclo_reconhecimento();
            ESP_LOGI(TAG, "aperte BOOT para reconhecer de novo");
            continue;
        }
#if FONTE_AUDIO_CLIPE
        vTaskDelay(pdMS_TO_TICKS(20));
#else
        ocioso_vu(&exibido);
        vTaskDelay(pdMS_TO_TICKS(40));
#endif
    }
}

void maquina_estado_iniciar(const char *ip, bool wifi_ok)
{
    if (ip) snprintf(s_ip, sizeof(s_ip), "%s", ip);
    s_wifi_ok = wifi_ok;
    // Core 1 (o core 0 fica com Wi-Fi/TCP-IP). Pilha grande: o Olaf usa ~4 KB só no buffer
    // do bloco, e o TLS do esp_http_client roda nesta task.
    xTaskCreatePinnedToCore(tarefa_principal, "shazamzin", 16384, NULL, 5, NULL, 1);
    ESP_LOGI(TAG, "task principal iniciada (IP=%s, wifi=%d, fonte=%s)", s_ip, wifi_ok,
             FONTE_AUDIO_CLIPE ? "clipe" : "microfone");
}
