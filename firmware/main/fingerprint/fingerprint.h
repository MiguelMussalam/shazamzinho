#pragma once
// Fingerprinting com o Olaf (componente components/olaf), mesmo código e perfil
// da libolaf.dll usada na ingestão do catálogo no PC.
#include <stdbool.h>
#include <stdint.h>
#include <stddef.h>

typedef struct {
    uint32_t hash;
    uint32_t timestamp_ms;
} fp_entry_t;

// Loga o perfil do Olaf (deve ser igual ao "profile" impresso pela CLI de ingestão).
void fingerprint_init(void);
const char *fingerprint_perfil(void);

// Sessão em fluxo: alimenta o Olaf aos poucos enquanto o áudio chega, sem guardar a
// captura inteira (5 s em float = 160 KB, mais do que o maior bloco livre do ESP32).
// O resultado é idêntico ao da extração do PC sobre o mesmo áudio (testado em OlafStreamTests).
typedef struct fp_sessao fp_sessao_t;

// Fingerprints vão para 'out' (até 'max'). Retorna NULL sem memória.
fp_sessao_t *fingerprint_sessao_nova(fp_entry_t *out, size_t max);
// PCM mono float32 normalizado (-1..1) a 8 kHz. Retorna false se o Olaf rejeitar a entrada.
bool fingerprint_sessao_alimentar(fp_sessao_t *s, const float *pcm, size_t n);
// Emite os últimos fingerprints, libera a sessão e retorna quantos foram guardados
// (o excedente acima de 'max' vai para *descartados, se não for NULL).
size_t fingerprint_sessao_finalizar(fp_sessao_t *s, size_t *descartados);

// Atalho: extrai de um buffer inteiro, alimentando a sessão em pedaços de 256 amostras.
size_t fingerprint_extrair_float(const float *pcm, size_t n, fp_entry_t *out, size_t max,
                                 size_t *descartados);
