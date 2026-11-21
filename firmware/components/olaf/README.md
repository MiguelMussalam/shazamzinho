# Componente Olaf (ESP-IDF)

Compila no ESP32 **os mesmos fontes** da `libolaf.dll` usada na ingestão do catálogo no PC:

- o Olaf clonado por `native/build.ps1` em `native/olaf/`, no commit de `native/olaf.version`;
- a ponte `native/olaf_wrapper.c` com o perfil de `native/shazamzin_olaf_config.h`
  (8 kHz, bloco 1024, passo 128, 2 event points).

Nada é copiado para cá: o `CMakeLists.txt` aponta para `native/` e **recusa compilar** se o
Olaf clonado estiver em outro commit. Assim firmware e catálogo não divergem.

Mesmas definições da DLL: `PFFFT_SIMD_DISABLE` e `-ffp-contract=off` (o Xtensa tem
multiplica-e-soma fundido; contrair mudaria o arredondamento em relação ao PC).

**Resultado medido em 2026-11-20:** em dois trechos de 5 s, o ESP32 gerou 74/74 e 119/119
fingerprints idênticos aos do PC (hash e tempo), em ~720 ms por trecho, com pico de uso de
~145 KB de heap. Ver `scripts/comparar-fingerprints-esp.ps1`.

## Pré-requisito

Antes da primeira build do firmware:

```powershell
./native/build.ps1
```
