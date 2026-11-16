#include "network.h"
#include "esp_log.h"

// TODO Fase 5: esp_http_client POST octet-stream + headers X-Device-ID/X-Api-Key.
static const char *TAG = "net";

void network_init(void)
{
    ESP_LOGW(TAG, "network_init: stub (Fase 5)");
}

int network_post_match(const uint8_t *payload, size_t len,
                       char *resp_json, size_t resp_cap)
{
    (void) payload;
    (void) len;
    (void) resp_json;
    (void) resp_cap;
    return -1;   // stub: ainda não envia ao servidor
}
