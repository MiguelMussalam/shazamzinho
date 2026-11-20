#include "network.h"
#include <string.h>
#include "esp_crt_bundle.h"
#include "esp_http_client.h"
#include "esp_log.h"
#include "config.h"

static const char *TAG = "net";

void network_init(void)
{
    ESP_LOGI(TAG, "servidor: %s", SERVER_URL);
}

int network_post_match(const uint8_t *payload, size_t len, char *resp_json, size_t resp_cap)
{
    if (resp_json && resp_cap) resp_json[0] = '\0';

    esp_http_client_config_t cfg = {
        .url = SERVER_URL,
        .method = HTTP_METHOD_POST,
        .timeout_ms = 15000,
        // HTTPS (túnel do Cloudflare): valida o certificado com o bundle de CAs do ESP-IDF.
        // Para http:// o bundle é simplesmente ignorado.
        .crt_bundle_attach = esp_crt_bundle_attach,
    };
    esp_http_client_handle_t client = esp_http_client_init(&cfg);
    if (!client) {
        ESP_LOGE(TAG, "esp_http_client_init falhou");
        return -1;
    }

    esp_http_client_set_header(client, "Content-Type", "application/octet-stream");
    esp_http_client_set_header(client, "X-Api-Key", API_KEY);
    esp_http_client_set_header(client, "X-Device-ID", DEVICE_ID);

    int status = -1;
    esp_err_t err = esp_http_client_open(client, (int) len);
    if (err != ESP_OK) {
        ESP_LOGE(TAG, "conexao falhou: %s", esp_err_to_name(err));
        goto fim;
    }
    if (esp_http_client_write(client, (const char *) payload, (int) len) != (int) len) {
        ESP_LOGE(TAG, "envio do payload incompleto");
        goto fim;
    }
    if (esp_http_client_fetch_headers(client) < 0) {
        ESP_LOGE(TAG, "sem resposta do servidor");
        goto fim;
    }

    status = esp_http_client_get_status_code(client);
    if (resp_json && resp_cap > 1) {
        int total = 0, lido;
        while (total < (int) resp_cap - 1 &&
               (lido = esp_http_client_read(client, resp_json + total, (int) resp_cap - 1 - total)) > 0) {
            total += lido;
        }
        resp_json[total] = '\0';
    }

fim:
    esp_http_client_close(client);
    esp_http_client_cleanup(client);
    return status;
}
