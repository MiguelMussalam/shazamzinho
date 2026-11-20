#include "fingerprint.h"
#include <stdlib.h>
#include "esp_log.h"
#include "olaf_wrapper.h"

static const char *TAG = "fp";

typedef struct {
    fp_entry_t *out;
    size_t max;
    size_t qtd;
    size_t descartados;
} coleta_t;

// Callback do Olaf: chamado uma vez por fingerprint, em ordem de tempo.
static int coletar(uint32_t hash, uint32_t timestamp_ms, void *user)
{
    coleta_t *c = (coleta_t *) user;
    if (c->qtd < c->max) {
        c->out[c->qtd].hash = hash;
        c->out[c->qtd].timestamp_ms = timestamp_ms;
        c->qtd++;
    } else {
        c->descartados++;
    }
    return 0; // nunca cancela
}

const char *fingerprint_perfil(void)
{
    return shazamzin_olaf_profile();
}

void fingerprint_init(void)
{
    ESP_LOGI(TAG, "Olaf pronto. perfil=%s (%d Hz)", shazamzin_olaf_profile(), shazamzin_olaf_sample_rate());
}

size_t fingerprint_extrair_float(const float *pcm, size_t n, fp_entry_t *out, size_t max,
                                 size_t *descartados)
{
    coleta_t c = { .out = out, .max = max, .qtd = 0, .descartados = 0 };
    int status = shazamzin_olaf_extract(pcm, n, coletar, &c);
    if (descartados) *descartados = c.descartados;
    if (status != 0) {
        // 1 = entrada inválida, 2 = sem memória, 3 = cancelado (ver olaf_wrapper.h)
        ESP_LOGE(TAG, "shazamzin_olaf_extract falhou: status=%d", status);
        return 0;
    }
    if (c.descartados) {
        ESP_LOGW(TAG, "%u fingerprints descartados (limite de %u)", (unsigned) c.descartados, (unsigned) max);
    }
    return c.qtd;
}

size_t fingerprint_process(const int16_t *samples, size_t n, fp_entry_t *out, size_t max)
{
    float *pcm = malloc(n * sizeof(float));
    if (!pcm) {
        ESP_LOGE(TAG, "sem memória para converter %u amostras", (unsigned) n);
        return 0;
    }
    for (size_t i = 0; i < n; i++) pcm[i] = samples[i] / 32768.0f;
    size_t qtd = fingerprint_extrair_float(pcm, n, out, max, NULL);
    free(pcm);
    return qtd;
}
