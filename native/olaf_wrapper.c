#include "olaf_wrapper.h"
#include <math.h>
#include <stdlib.h>
#include <string.h>
#include "olaf_config.h"
#include "olaf_fft.h"
#include "olaf_ep_extractor.h"
#include "olaf_fp_extractor.h"
#include "shazamzin_olaf_config.h"

const char *shazamzin_olaf_profile(void) {
    return SHAZAMZIN_PROFILE OLAF_REVISION;
}
int shazamzin_olaf_sample_rate(void) { return SHAZAMZIN_SAMPLE_RATE; }

static int emit(Olaf_FP_Extractor *fp, struct extracted_event_points *eps, int block,
    sz_fingerprint_callback callback, void *user) {
    if (!eps || eps->eventPointIndex == 0) return 0;
    struct extracted_fingerprints *fps = olaf_fp_extractor_extract(fp, eps, block);
    for (size_t i = 0; i < fps->fingerprintIndex; i++) {
        struct fingerprint f = fps->fingerprints[i];
        uint64_t raw_hash = olaf_fp_extractor_hash(f);
        /* Projeção explícita para o protocolo v1; upstream pode exceder 32 bits. */
        uint32_t hash = (uint32_t)(raw_hash ^ (raw_hash >> 32));
        uint32_t ms = (uint32_t)((uint64_t)f.timeIndex1 * SHAZAMZIN_STEP_SIZE * 1000 / SHAZAMZIN_SAMPLE_RATE);
        if (callback(hash, ms, user)) { fps->fingerprintIndex = 0; return 3; }
    }
    fps->fingerprintIndex = 0;
    return 0;
}

int shazamzin_olaf_extract(const float *samples, size_t count,
    sz_fingerprint_callback callback, void *user) {
    /* Olaf usa um marcador de tempo 1<<23. Mantemos o áudio abaixo dele. */
    if (!callback || (!samples && count) || count / SHAZAMZIN_STEP_SIZE >= (1u << 23) - 100) return 1;
    /* Float PCM pode exceder +/-1 após decodificação MP3/resampling; isso não é corrupção. */
    for (size_t i = 0; i < count; i++) if (!isfinite(samples[i])) return 1;
    if (count < 1024) return 0;
    int status = 2;
    Olaf_Config *config = shazamzin_olaf_config_new();
    Olaf_FFT *fft = NULL;
    Olaf_EP_Extractor *ep = NULL;
    Olaf_FP_Extractor *fp = NULL;
    if (!config) goto cleanup;
    fft = olaf_fft_new(config);
    ep = olaf_ep_extractor_new(config);
    fp = olaf_fp_extractor_new(config);
    if (!fft || !ep || !fp) goto cleanup;
    float block_samples[1024];
    struct extracted_event_points *eps = NULL;
    int block = 0;
    status = 0;
    for (size_t offset = 0; offset + SHAZAMZIN_BLOCK_SIZE <= count; offset += SHAZAMZIN_STEP_SIZE, block++) {
        memcpy(block_samples, samples + offset, sizeof(block_samples));
        olaf_fft_forward(fft, block_samples);
        eps = olaf_ep_extractor_extract(ep, block_samples, block);
        if (eps->eventPointIndex > config->eventPointThreshold) {
            status = emit(fp, eps, block, callback, user);
            if (status) goto cleanup;
        }
    }
    status = emit(fp, eps, block, callback, user);
cleanup:
    olaf_fp_extractor_destroy(fp);
    olaf_ep_extractor_destroy(ep);
    olaf_fft_destroy(fft);
    olaf_config_destroy(config);
    return status;
}

/* ---- Extração em fluxo (mesma sequência de chamadas de shazamzin_olaf_extract) ---- */

struct sz_stream {
    Olaf_Config *config;
    Olaf_FFT *fft;
    Olaf_EP_Extractor *ep;
    Olaf_FP_Extractor *fp;
    sz_fingerprint_callback callback;
    void *user;
    struct extracted_event_points *eps;
    float window[SHAZAMZIN_BLOCK_SIZE]; /* últimas amostras recebidas */
    float block[SHAZAMZIN_BLOCK_SIZE];  /* cópia que a FFT sobrescreve */
    size_t filled;
    size_t total;
    int blocks;
    int status;
};

SZ_API sz_stream *shazamzin_olaf_stream_new(sz_fingerprint_callback callback, void *user) {
    if (!callback) return NULL;
    sz_stream *s = calloc(1, sizeof(*s));
    if (!s) return NULL;
    s->callback = callback;
    s->user = user;
    s->config = shazamzin_olaf_config_new();
    if (s->config) {
        s->fft = olaf_fft_new(s->config);
        s->ep = olaf_ep_extractor_new(s->config);
        s->fp = olaf_fp_extractor_new(s->config);
    }
    if (!s->config || !s->fft || !s->ep || !s->fp) {
        s->status = 2;
        shazamzin_olaf_stream_finish(s);
        return NULL;
    }
    return s;
}

SZ_API int shazamzin_olaf_stream_feed(sz_stream *s, const float *samples, size_t count) {
    if (!s) return 1;
    if (s->status) return s->status;
    if (!samples && count) return s->status = 1;
    for (size_t i = 0; i < count; i++) {
        if (!isfinite(samples[i])) return s->status = 1;
        if (++s->total / SHAZAMZIN_STEP_SIZE >= (1u << 23) - 100) return s->status = 1;
        s->window[s->filled++] = samples[i];
        if (s->filled < SHAZAMZIN_BLOCK_SIZE) continue;

        /* Bloco completo: idêntico ao corpo do laço de shazamzin_olaf_extract. */
        memcpy(s->block, s->window, sizeof(s->block));
        olaf_fft_forward(s->fft, s->block);
        s->eps = olaf_ep_extractor_extract(s->ep, s->block, s->blocks);
        if (s->eps->eventPointIndex > s->config->eventPointThreshold) {
            s->status = emit(s->fp, s->eps, s->blocks, s->callback, s->user);
        }
        s->blocks++;
        /* Desliza um passo: o próximo bloco começa SHAZAMZIN_STEP_SIZE amostras depois. */
        memmove(s->window, s->window + SHAZAMZIN_STEP_SIZE,
                (SHAZAMZIN_BLOCK_SIZE - SHAZAMZIN_STEP_SIZE) * sizeof(float));
        s->filled = SHAZAMZIN_BLOCK_SIZE - SHAZAMZIN_STEP_SIZE;
        if (s->status) return s->status;
    }
    return 0;
}

SZ_API int shazamzin_olaf_stream_finish(sz_stream *s) {
    if (!s) return 1;
    int status = s->status;
    /* extract só emite o final se houve ao menos um bloco (áudio >= 1024 amostras). */
    if (!status && s->blocks > 0) status = emit(s->fp, s->eps, s->blocks, s->callback, s->user);
    olaf_fp_extractor_destroy(s->fp);
    olaf_ep_extractor_destroy(s->ep);
    olaf_fft_destroy(s->fft);
    olaf_config_destroy(s->config);
    free(s);
    return status;
}
