#include "network.h"
#include "esp_log.h"
// TODO Fase 5: esp_http_client POST octet-stream + headers X-Device-ID/X-Api-Key.
static const char *TAG = "net";
void network_init(void) { ESP_LOGW(TAG, "network_init: stub (Fase 5)"); }
int network_post_match(const uint8_t *p, size_t l, char *r, size_t c) { (void)p;(void)l;(void)r;(void)c; return -1; }
