#include "olaf_wrapper.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <inttypes.h>
static int print_fp(uint32_t hash, uint32_t ms, void *user) {
    (void)user;
    return printf("%" PRIu32 ",%" PRIu32 "\n", hash, ms) < 0;
}
int main(int argc, char **argv) {
    if (argc == 2 && strcmp(argv[1], "--profile") == 0) {
        puts(shazamzin_olaf_profile()); return 0;
    }
    if (argc != 2) { fprintf(stderr, "Uso: shazamzin-olaf audio.f32 (mono float32 LE, 8kHz)\n"); return 1; }
    FILE *file = fopen(argv[1], "rb");
    if (!file) { perror("PCM"); return 1; }
    if (fseek(file, 0, SEEK_END)) { fclose(file); return 1; }
    long bytes = ftell(file);
    rewind(file);
    if (bytes < 0 || bytes % sizeof(float)) { fclose(file); return 1; }
    float *samples = malloc(bytes ? (size_t)bytes : sizeof(float));
    if (!samples) { fclose(file); return 2; }
    if (fread(samples, 1, (size_t)bytes, file) != (size_t)bytes) {
        free(samples); fclose(file); return 1;
    }
    fclose(file);
    int status = shazamzin_olaf_extract(samples, (size_t)bytes / sizeof(float), print_fp, NULL);
    free(samples);
    return status;
}
