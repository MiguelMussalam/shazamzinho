# Clipe de teste (áudio sem microfone)

Com `FONTE_AUDIO_CLIPE = 1` no `config.h`, o firmware processa este trecho fixo em vez do
microfone: Olaf no ESP → `POST /match` → resultado no serial (e no OLED, se houver).

| Arquivo | O que é |
|---|---|
| `clipe_teste.c` | **Gerado.** PCM float32 mono 8 kHz (5 s) em literais hexadecimais, bit a bit igual ao que o PC entrega ao Olaf |
| `clipe_teste.pc.bin` | Fingerprints que o PC calculou para o mesmo PCM (payload v1). Referência do comparador |
| `clipe_teste.h` | Declarações usadas pelo firmware |

Gerar de outra música (ela precisa estar no catálogo para ser reconhecida):

```powershell
./scripts/gerar-clipe-teste.ps1 -Path "C:\shazamzinho-dados\jamendo\audio\00\1028800.low.mp3" -OffsetSeconds 60 -Label "Time Tellers - Anitek"
```

Comparar os fingerprints do ESP com os do PC (critério do marco M2: ≥ 90% iguais):

```powershell
./scripts/comparar-fingerprints-esp.ps1 -Port COM8
```

## Atribuição

O trecho atual é de **"Time Tellers", de Anitek** (segundos 60 a 65), do
[Jamendo](http://www.jamendo.com/track/1028800), distribuído no dataset
[MTG-Jamendo](https://mtg.github.io/mtg-jamendo-dataset/) sob a licença
[Creative Commons Atribuição-NãoComercial-CompartilhaIgual 3.0](http://creativecommons.org/licenses/by-nc-sa/3.0/).
Este trecho (`clipe_teste.c` e `clipe_teste.pc.bin`) é redistribuído sob a mesma licença,
apenas para fins acadêmicos e não comerciais.
