#include "audio_capture.h"
#include "esp_log.h"
// TODO Fase 4: i2s_std_new_channel + DMA ring buffer. Validar via UDP+ffplay antes.
static const char *TAG = "audio";
void audio_capture_init(void) { ESP_LOGW(TAG, "audio_capture_init: stub (Fase 4)"); }
size_t audio_capture_read(int16_t *out, size_t max_samples) { (void)out; (void)max_samples; return 0; }
