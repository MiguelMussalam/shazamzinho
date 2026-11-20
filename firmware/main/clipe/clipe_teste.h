#pragma once
// Trecho de música usado quando FONTE_AUDIO_CLIPE = 1 (sem microfone).
// clipe_teste.c é GERADO por scripts/gerar-clipe-teste.ps1: PCM float32 mono 8 kHz,
// bit a bit igual ao que o PC entrega ao Olaf (extract --raw-output).
#include <stddef.h>

extern const float clipe_teste[];
extern const size_t clipe_teste_amostras;
extern const char clipe_teste_origem[]; // música, offset e perfil usados na geração
