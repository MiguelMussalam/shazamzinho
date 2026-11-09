#ifndef SHAZAMZIN_OLAF_CONFIG_H
#define SHAZAMZIN_OLAF_CONFIG_H
#include <stdlib.h>
#include "olaf_config.h"

/* Perfil compartilhável com o firmware. Alterações exigem outro perfil e reindexação. */
#define SHAZAMZIN_SAMPLE_RATE 8000
#define SHAZAMZIN_BLOCK_SIZE 1024
#define SHAZAMZIN_STEP_SIZE 128
#define SHAZAMZIN_PROFILE "shazamzin-8k-1024-128-2ep-fold32-v1/"
static inline Olaf_Config *shazamzin_olaf_config_new(void) {
    Olaf_Config *c = calloc(1, sizeof(*c));
    if (!c) return NULL;
    c->audioSampleRate = SHAZAMZIN_SAMPLE_RATE;
    c->audioBlockSize = SHAZAMZIN_BLOCK_SIZE;
    c->audioStepSize = SHAZAMZIN_STEP_SIZE;
    c->bytesPerAudioSample = 4;
    c->maxEventPoints = 100;
    c->eventPointThreshold = 30;
    c->filterSizeFrequency = 103;
    c->halfFilterSizeFrequency = 51;
    c->filterSizeTime = 24;
    c->halfFilterSizeTime = 12;
    c->minEventPointMagnitude = 0.001f;
    c->minFrequencyBin = 9;
    c->maxEventPointUsages = 20;
    c->numberOfEPsPerFP = 2;
    c->minTimeDistance = 2;
    c->maxTimeDistance = 33;
    c->minFreqDistance = 1;
    c->maxFreqDistance = 128;
    c->maxFingerprints = 300;
    return c;
}
#endif
