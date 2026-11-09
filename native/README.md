# Ponte Olaf para o Shazamzin

`build.ps1` clona o [Olaf](https://github.com/JorenSix/Olaf), seleciona exatamente
o commit de `olaf.version` e compila somente os componentes de extração. Fontes
upstream ficam ignorados em `olaf/`; os builds ficam em `build/`.

O perfil está explícito em `shazamzin_olaf_config.h`: mono 8 kHz, FFT 1024,
passo 128 (16 ms), dois event points por fingerprint, filtro temporal de 24 blocos,
limites de 100 event points e 300 fingerprints por lote. PFFFT usa implementação
escalar e o build GCC desativa contração de operações de ponto flutuante.

Esse perfil adapta o tempo do preset ESP32 do upstream (16 kHz com passo 256)
ao sample rate de 8 kHz escolhido pelo projeto. Não é o preset padrão do Olaf.
O hash do upstream é projetado explicitamente por `uint32(h ^ (h >> 32))` para o
campo uint32 do protocolo v1. O timestamp é `timeIndex1 * 128 * 1000 / 8000`.

O identificador `shazamzin-8k-1024-128-2ep-fold32-v1/<commit>` é salvo no catálogo.
Qualquer mudança que altere hashes ou timestamps precisa de outro identificador
e de reindexação. O firmware deverá usar a mesma revisão, configuração e projeção;
a equivalência entre PC e ESP32 ainda precisa ser validada no hardware.

## ABI

`olaf_wrapper.h` expõe configuração e extração de um array PCM mono float32.
Cada chamada tem sua própria sessão, destruída antes do retorno. Fingerprints
são entregues por callback, com hash e timestamp_ms uint32. O retorno é 0 para
sucesso, 1 para entrada inválida, 2 para falha de alocação e 3 para cancelamento
pedido pelo callback. O chamador mantém o PCM e o callback válidos até o retorno.

`shazamzin-olaf.exe audio.f32` imprime `hash,timestamp_ms` em CSV. É a CLI da
ponte, usada para conferir a fronteira C/.NET; não é o executável Zig do upstream.
Use `extract --raw-output` na CLI .NET para obter o PCM correspondente a uma consulta.

O Olaf é distribuído sob AGPL-3.0-or-later. O checkout inclui sua licença e as
licenças dos componentes de terceiros, incluindo PFFFT. Consulte os fontes
fixados ao redistribuir a biblioteca.
