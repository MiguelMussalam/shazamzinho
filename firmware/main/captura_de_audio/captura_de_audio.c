#include "captura_de_audio.h"
#include "driver/i2s_std.h"
#include "esp_log.h"

// Captura do microfone INMP441 via I2S (nova API i2s_std do ESP-IDF 5.x).
// Pinos: WS=GPIO15, SCK=GPIO14, SD=GPIO32. 8000 Hz, mono, canal esquerdo
// (L/R do módulo ligado ao GND). Ver docs/esp-idf-notes.md.

#define I2S_BCLK_GPIO   14   // SCK
#define I2S_WS_GPIO     15   // WS  (pino de strapping — conferir boot com o mic ligado)
#define I2S_DIN_GPIO    32   // SD
#define TAXA_AMOSTRAGEM 8000

// O INMP441 entrega 24 bits dentro de um slot de 32. Deslocamos para int16.
// >>14 dá uma sensibilidade razoável para o VU; ajuste na bancada se precisar.
#define DESLOCAMENTO_AMOSTRA 14

static const char *TAG = "audio";
static i2s_chan_handle_t s_canal_rx = NULL;

void captura_audio_iniciar(void)
{
    i2s_chan_config_t cfg_canal = I2S_CHANNEL_DEFAULT_CONFIG(I2S_NUM_0, I2S_ROLE_MASTER);
    esp_err_t err = i2s_new_channel(&cfg_canal, NULL, &s_canal_rx);
    if (err != ESP_OK) {
        ESP_LOGE(TAG, "i2s_new_channel falhou: %s", esp_err_to_name(err));
        return;
    }

    i2s_std_config_t cfg_std = {
        .clk_cfg  = I2S_STD_CLK_DEFAULT_CONFIG(TAXA_AMOSTRAGEM),
        .slot_cfg = I2S_STD_PHILIPS_SLOT_DEFAULT_CONFIG(
            I2S_DATA_BIT_WIDTH_32BIT, I2S_SLOT_MODE_MONO),
        .gpio_cfg = {
            .mclk = I2S_GPIO_UNUSED,
            .bclk = I2S_BCLK_GPIO,
            .ws   = I2S_WS_GPIO,
            .dout = I2S_GPIO_UNUSED,
            .din  = I2S_DIN_GPIO,
            .invert_flags = { .mclk_inv = false, .bclk_inv = false, .ws_inv = false },
        },
    };
    // L/R no GND => canal esquerdo.
    cfg_std.slot_cfg.slot_mask = I2S_STD_SLOT_LEFT;

    err = i2s_channel_init_std_mode(s_canal_rx, &cfg_std);
    if (err != ESP_OK) {
        ESP_LOGE(TAG, "init_std_mode falhou: %s", esp_err_to_name(err));
        i2s_del_channel(s_canal_rx);
        s_canal_rx = NULL;
        return;
    }

    ESP_ERROR_CHECK(i2s_channel_enable(s_canal_rx));
    ESP_LOGI(TAG, "I2S INMP441 pronto (%d Hz, mono, canal esq.)", TAXA_AMOSTRAGEM);
}

size_t captura_audio_ler(int16_t *saida, size_t max_amostras)
{
    if (!s_canal_rx || !saida || max_amostras == 0) return 0;

    // Lê em blocos de até 256 amostras de 32 bits para um buffer temporário.
    int32_t bruto[256];
    size_t desejado = max_amostras < 256 ? max_amostras : 256;
    size_t bytes_lidos = 0;

    esp_err_t err = i2s_channel_read(s_canal_rx, bruto, desejado * sizeof(int32_t),
                                     &bytes_lidos, 100 /* ms */);
    if (err != ESP_OK) {
        ESP_LOGW(TAG, "i2s_channel_read: %s", esp_err_to_name(err));
        return 0;
    }

    size_t qtd = bytes_lidos / sizeof(int32_t);
    for (size_t i = 0; i < qtd; i++) {
        saida[i] = (int16_t) (bruto[i] >> DESLOCAMENTO_AMOSTRA);
    }
    return qtd;
}
