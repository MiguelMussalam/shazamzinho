#ifndef SHAZAMZIN_OLAF_WRAPPER_H
#define SHAZAMZIN_OLAF_WRAPPER_H
#include <stddef.h>
#include <stdint.h>
#ifdef _WIN32
#ifdef SHAZAMZIN_OLAF_BUILD
#define SZ_API __declspec(dllexport)
#else
#define SZ_API __declspec(dllimport)
#endif
#else
#define SZ_API __attribute__((visibility("default")))
#endif
/* Retorne != 0 para cancelar. Timestamps relativos ao início do PCM, em ms. */
typedef int (*sz_fingerprint_callback)(uint32_t hash, uint32_t timestamp_ms, void *user);
SZ_API const char *shazamzin_olaf_profile(void);
SZ_API int shazamzin_olaf_sample_rate(void);
/* PCM normalizado, mono float32 a 8 kHz. 0=OK, 1=entrada inválida, 2=OOM, 3=cancelado. */
SZ_API int shazamzin_olaf_extract(const float *samples, size_t count,
    sz_fingerprint_callback callback, void *user);
#endif
