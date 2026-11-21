# Teste integrado — ESP32 + microfone + OLED + servidor

Roteiro para testar tudo junto (e para o dia da apresentação). Tudo o que dava para validar sem
o microfone e o OLED já foi validado em 2026-11-20 — ver `docs/sprint-log.md`. O que falta
provar aqui é o hardware: o microfone captando bem e o OLED mostrando o resultado.

## 1. Montagem (ESP32 desligado)

1. Monte conforme [`docs/fritzing/README.md`](fritzing/README.md): INMP441 no vão central,
   `L/R` no GND, OLED em `SDA=GPIO21` / `SCL=GPIO22`, ambos em **3,3 V** (nunca 5 V).
2. Com o multímetro: continuidade dos trilhos e **nenhum curto entre 3,3 V e GND**.

## 2. Servidor (no notebook)

```powershell
# Docker Desktop aberto antes
./scripts/subir-servidor.ps1
```

Ele sobe o Valkey, a API (com o catálogo de 586 músicas) e o túnel, e imprime:

```
Para firmware/main/config.h:
  #define SERVER_URL       "https://<aleatorio>.trycloudflare.com/match"
  #define API_KEY          "<chave>"
```

**Deixe essa janela aberta.** Se o servidor reiniciar, o túnel ganha outra URL e o ESP
precisa ser gravado de novo.

## 3. Firmware

Em `firmware/main/config.h` (copie de `config.h.example` se não existir):

| Item | Valor |
|---|---|
| `WIFI_SSID` / `WIFI_PASSWORD` | hotspot do celular, em **2,4 GHz** |
| `SERVER_URL` / `API_KEY` | as duas linhas impressas pelo `subir-servidor.ps1` |
| `FONTE_AUDIO_CLIPE` | `0` (microfone) |
| `BATCH_WINDOW_MS` | `5000` |
| `GANHO_MIC` | `40.0f` para começar |

```powershell
cd firmware
pio run -t upload -t monitor      # ou C:\Users\<voce>\.platformio\penv\Scripts\platformio.exe
```

Se o `pio` não estiver no PATH, use o caminho completo acima. Antes da primeira build numa
máquina nova: `./native/build.ps1` (o firmware compila os fontes do Olaf clonados por ele).

## 4. O que conferir no serial e no OLED

No boot (antes de apertar qualquer coisa):

- [ ] `OLED SSD1306 pronto` — se aparecer `OLED nao respondeu em 0x3C`, confira SDA/SCL/VCC/GND.
  O log lista os endereços I²C que responderam (alguns módulos usam `0x3D`).
- [ ] `IP obtido: ...` e o IP no OLED.
- [ ] `I2S INMP441 pronto` e a **barra de VU** do OLED reagindo a palmas e à fala.
- [ ] Um primeiro ciclo roda sozinho no boot. Em silêncio ele termina em "não reconhecida".

## 5. Reconhecer uma música

1. Abra `C:\shazamzinho-dados\playlist-teste.m3u` (gerada por `./scripts/gerar-playlist.ps1`;
   só músicas do catálogo são reconhecidas).
2. Toque uma faixa com volume médio, caixa de som a ~30–50 cm do microfone.
3. Aperte **BOOT** no ESP32. São 5 s ouvindo (`Ouvindo 5 s...`) e ~3 s de consulta.
4. O OLED mostra título, artista e a confiança; o serial mostra a linha `RESULTADO match=1 ...`.

A cada captura o serial também mostra o nível do áudio:

```
captura: 5.0 s, ganho 40x, pico -9.5 dBFS, rms -24.0 dBFS, saturadas 0.00%
```

## 6. Diagnóstico

| Sintoma no serial | Causa provável | O que fazer |
|---|---|---|
| `audio muito baixo` / pico abaixo de -30 dBFS | ganho baixo ou caixa longe | aumente `GANHO_MIC` (ex.: 80) e regrave; aproxime a caixa |
| `muitas amostras saturadas` (> 1%) | ganho alto ou volume alto | diminua `GANHO_MIC` (ex.: 20) ou o volume |
| pico em `-120 dBFS` sempre, VU parado | microfone sem sinal | confira `SD=GPIO32`, `SCK=GPIO14`, `WS=GPIO15`, `L/R` no GND, 3,3 V |
| `Olaf: 0 fingerprints` com nível bom | áudio sem conteúdo musical (só ruído) | toque música, não fala |
| `match=0` com centenas de hashes recebidos e poucos acertados | ruído alto ou música fora do catálogo | use a playlist; reduza o ruído ambiente; teste outra faixa |
| `HTTP -1` / `sem conexao` | sem internet no hotspot ou URL antiga | confira o hotspot; se o servidor reiniciou, atualize `SERVER_URL` e regrave |
| `HTTP 401` | `API_KEY` diferente da do servidor | copie de novo a linha impressa pelo `subir-servidor.ps1` |
| ESP não termina o boot com o microfone ligado | `WS` no GPIO15 (pino de strapping) | desconecte o fio do WS, grave/ligue, reconecte |

## 7. Plano B

- **Sem microfone funcionando:** grave com `FONTE_AUDIO_CLIPE 1`. O ESP processa um trecho de
  "Time Tellers" (Anitek) guardado na flash e reconhece pela mesma rede/servidor.
  Mostra o sistema inteiro menos a captura.
- **Sem internet na sala:** ligue o notebook no mesmo hotspot e use
  `./scripts/subir-servidor.ps1 -Tunnel:$false`. Ele mostra `http://<ip>:5184/match` para o
  `SERVER_URL`. O firewall do Windows pode bloquear a porta 5184 (libere ou volte ao túnel).
- Testar o servidor sozinho, sem o ESP: `./scripts/testar-reconhecimento.ps1 -Count 10 -DurationSeconds 5`.

## 8. No dia da apresentação

- [ ] Notebook na tomada, sem suspender. Docker aberto.
- [ ] `subir-servidor.ps1` rodando **antes** de sair de casa; ESP gravado com a URL dele.
- [ ] Hotspot do celular ligado em 2,4 GHz, com dados móveis.
- [ ] Playlist aberta; uma música testada no local antes de começar.
- [ ] Plano B ensaiado: trocar para `FONTE_AUDIO_CLIPE 1` e regravar leva ~1 min.
