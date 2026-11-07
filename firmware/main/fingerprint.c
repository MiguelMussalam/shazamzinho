#include "fingerprint.h"
#include "esp_log.h"
// TODO Fase 4: chamar Olaf (components/olaf). Hashes devem bater com a ingestão.
static const char *TAG = "fp";
void fingerprint_init(void) { ESP_LOGW(TAG, "fingerprint_init: stub (Fase 4)"); }
size_t fingerprint_process(const int16_t *s, size_t n, fp_entry_t *o, size_t m) { (void)s;(void)n;(void)o;(void)m; return 0; }
