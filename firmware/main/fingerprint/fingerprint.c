#include "fingerprint.h"
#include <stdlib.h>
#include "esp_log.h"
#include "olaf_wrapper.h"

static const char *TAG = "fp";

struct fp_sessao {
    sz_stream *stream;
    fp_entry_t *out;
    size_t max;
    size_t qtd;
    size_t descartados;
};

// Callback do Olaf: chamado uma vez por fingerprint, em ordem de tempo.
static int coletar(uint32_t hash, uint32_t timestamp_ms, void *user)
{
    fp_sessao_t *s = (fp_sessao_t *) user;
    if (s->qtd < s->max) {
        s->out[s->qtd].hash = hash;
        s->out[s->qtd].timestamp_ms = timestamp_ms;
        s->qtd++;
    } else {
        s->descartados++;
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

fp_sessao_t *fingerprint_sessao_nova(fp_entry_t *out, size_t max)
{
    fp_sessao_t *s = calloc(1, sizeof(*s));
    if (!s) return NULL;
    s->out = out;
    s->max = max;
    s->stream = shazamzin_olaf_stream_new(coletar, s);
    if (!s->stream) {
        ESP_LOGE(TAG, "sem memoria para a sessao do Olaf");
        free(s);
        return NULL;
    }
    return s;
}

bool fingerprint_sessao_alimentar(fp_sessao_t *s, const float *pcm, size_t n)
{
    int status = shazamzin_olaf_stream_feed(s->stream, pcm, n);
    if (status != 0) ESP_LOGE(TAG, "Olaf rejeitou o audio: status=%d", status);
    return status == 0;
}

size_t fingerprint_sessao_finalizar(fp_sessao_t *s, size_t *descartados)
{
    if (!s) return 0;
    // 1 = entrada inválida, 2 = sem memória, 3 = cancelado (ver olaf_wrapper.h)
    int status = shazamzin_olaf_stream_finish(s->stream);
    size_t qtd = s->qtd;
    if (status != 0) ESP_LOGE(TAG, "Olaf terminou com status=%d", status);
    if (s->descartados) {
        ESP_LOGW(TAG, "%u fingerprints descartados (limite de %u)", (unsigned) s->descartados, (unsigned) s->max);
    }
    if (descartados) *descartados = s->descartados;
    free(s);
    return qtd;
}

size_t fingerprint_extrair_float(const float *pcm, size_t n, fp_entry_t *out, size_t max,
                                 size_t *descartados)
{
    fp_sessao_t *s = fingerprint_sessao_nova(out, max);
    if (!s) return 0;
    for (size_t i = 0; i < n; i += 256) {
        size_t pedaco = n - i < 256 ? n - i : 256;
        if (!fingerprint_sessao_alimentar(s, pcm + i, pedaco)) break;
    }
    return fingerprint_sessao_finalizar(s, descartados);
}
