#include "resposta.h"
#include <string.h>
#include "cJSON.h"

static int inteiro(const cJSON *raiz, const char *nome)
{
    const cJSON *v = cJSON_GetObjectItemCaseSensitive(raiz, nome);
    return cJSON_IsNumber(v) ? v->valueint : 0;
}

static double real(const cJSON *raiz, const char *nome)
{
    const cJSON *v = cJSON_GetObjectItemCaseSensitive(raiz, nome);
    return cJSON_IsNumber(v) ? v->valuedouble : 0.0;
}

static void texto(const cJSON *raiz, const char *nome, char *dst, size_t cap)
{
    const cJSON *v = cJSON_GetObjectItemCaseSensitive(raiz, nome);
    dst[0] = '\0';
    if (cJSON_IsString(v) && v->valuestring) {
        strncpy(dst, v->valuestring, cap - 1);
        dst[cap - 1] = '\0';
    }
}

bool resposta_ler(const char *json, resposta_t *r)
{
    memset(r, 0, sizeof(*r));
    cJSON *raiz = cJSON_Parse(json);
    if (!raiz) return false;

    const cJSON *match = cJSON_GetObjectItemCaseSensitive(raiz, "match");
    bool ok = cJSON_IsBool(match);
    if (ok) {
        r->match = cJSON_IsTrue(match);
        r->song_id = inteiro(raiz, "song_id");
        texto(raiz, "title", r->titulo, sizeof(r->titulo));
        texto(raiz, "artist", r->artista, sizeof(r->artista));
        r->confianca = real(raiz, "confidence");
        r->offset_s = real(raiz, "matched_offset_sec");
        r->tempo_servidor_ms = inteiro(raiz, "processing_time_ms");
        r->hashes_recebidos = inteiro(raiz, "hashes_received");
        r->hashes_acertados = inteiro(raiz, "hashes_hit");
    }
    cJSON_Delete(raiz);
    return ok;
}
