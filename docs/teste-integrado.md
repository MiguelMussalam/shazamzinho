# Teste integrado — ESP32 + microfone + OLED + servidor

Roteiro para testar tudo junto (e para o dia da apresentação). Tudo o que dava para validar sem
o microfone e o OLED já foi validado: fingerprint em 2026-11-20 (ver `docs/sprint-log.md`) e, em
2026-11-22, ESP32 em modo clipe → Wi-Fi → túnel → API, 4 de 4 reconhecidas em 3,6–4,4 s. O que falta
provar aqui é o hardware: o microfone captando bem e o OLED mostrando o resultado.

## 1. Montagem (ESP32 desligado)

1. Monte conforme [`docs/fritzing/README.md`](fritzing/README.md): INMP441 no vão central,
   `L/R` no GND, OLED em `SDA=GPIO21` / `SCL=GPIO22`, ambos em **3,3 V** (nunca 5 V).
2. Com o multímetro: continuidade dos trilhos e **nenhum curto entre 3,3 V e GND**.

## 2. Servidor (no notebook)

Só quem roda o servidor (o notebook com o catálogo):

```powershell
# Docker Desktop aberto antes
./scripts/subir-servidor.ps1 -Publicar
git add firmware/main/servidor.h; git commit -m "chore: endereço do servidor"; git push
```

Ele sobe o Valkey, a API (com o catálogo de 586 músicas) e o túnel, e grava a **URL** em
`firmware/main/servidor.h`, que é versionado. A **chave** fica em `api-key.secret` (fora do git):
mande-a em particular para quem vai gravar o ESP. Ela não muda entre reinícios.
**Deixe essa janela aberta** e não a projete: ela mostra a chave. Se o servidor reiniciar, o túnel ganha outra URL: publique de
novo, e quem grava o ESP faz `git pull` e regrava.

## 3. Firmware (pode ser em outra máquina)

```powershell
git checkout main
git pull                                                   # pega o servidor.h atual

# o servidor está no ar? (deve mostrar status ok e 586 músicas; se falhar, avise quem o roda)
$u = [regex]::Match((Get-Content firmware/main/servidor.h -Raw), '(?m)^#define SERVIDOR_URL\s+"([^"]+)/match"').Groups[1].Value
Invoke-RestMethod "$u/health"

if (-not (Test-Path firmware/main/config.h)) { Copy-Item firmware/main/config.h.example firmware/main/config.h }
```

Em `firmware/main/config.h`:

| Item | Valor |
|---|---|
| `WIFI_SSID` / `WIFI_PASSWORD` | Wi-Fi do local, em **2,4 GHz** |
| `API_KEY` | a chave recebida em particular de quem roda o servidor |
| `DEVICE_ID` | um nome para a placa (aparece no histórico do servidor) |
| `FONTE_AUDIO_CLIPE` | `0` (microfone) |
| `BATCH_WINDOW_MS` | `5000` |
| `GANHO_MIC` | `40.0f` para começar |

Faça o `git pull` **logo antes de testar**: a URL muda quando o servidor reinicia.
O endereço do servidor **não** vai no `config.h`: vem do `servidor.h`. Se o seu `config.h`
é antigo, recrie a partir do `config.h.example` (ele não tinha ganho nem 5 s); sem `API_KEY`
o firmware nem compila, com uma mensagem dizendo onde colocar.

```powershell
cd firmware
pio run -t upload -t monitor      # ou C:\Users\<voce>\.platformio\penv\Scripts\platformio.exe
```

Se o `pio` não estiver no PATH, use o caminho completo acima. Na primeira build o firmware
baixa sozinho o Olaf no commit fixado (só precisa de git e internet).

## 4. O que conferir no serial e no OLED

No boot (antes de apertar qualquer coisa):

- [ ] `OLED SSD1306 pronto` — se aparecer `OLED nao respondeu em 0x3C`, confira SDA/SCL/VCC/GND.
  O log lista os endereços I²C que responderam (alguns módulos usam `0x3D`).
- [ ] `IP obtido: ...` e o IP no OLED.
- [ ] `I2S INMP441 pronto` e a **barra de VU** do OLED reagindo a palmas e à fala.
- [ ] Um primeiro ciclo roda sozinho no boot. Em silêncio ele termina em "não reconhecida".

Se o Wi-Fi falhar no boot (`sem Wi-Fi`), ligue a rede e aperte **EN/RST**: o BOOT repete o
reconhecimento, mas não tenta conectar de novo.

## 5. Reconhecer uma música

1. Use as cinco músicas abaixo. Os MP3 não vão para o repositório: peça a quem roda o servidor
   (`C:\shazamzinho-dados\jamendo\audio\00\`). Só músicas do catálogo são reconhecidas.

   | Arquivo | Música |
   |---|---|
   | `431500.low.mp3` | Vivaldi, sonata para flauta e oboé (Carlo Tamponi); o título sai cortado no OLED |
   | `1110000.low.mp3` | Who Jam (Podington Bear) |
   | `1194700.low.mp3` | Händel, Messias nº 16 (Musica in Armonia) |
   | `1167300.low.mp3` | Nackskott (Ozzed) |
   | `1269900.low.mp3` | Glee Club Polka (Kevin MacLeod) |

2. Toque uma faixa com volume médio, caixa de som a ~30–50 cm do microfone.
3. Aperte **BOOT** no ESP32. São 5 s ouvindo (`Ouvindo 5 s...`) e ~3 s de consulta.
4. O OLED mostra título, artista e a confiança; o serial mostra a linha `RESULTADO match=1 ...`.

A cada captura o serial também mostra o nível do áudio:

```
captura: 5.0 s, ganho 40x, pico -9.5 dBFS, rms -24.0 dBFS, saturadas 0.00%
```

### Medir o ruído real (faça antes das tentativas)

1. Ignore a captura automática do boot (o microfone pode ainda estar inicializando).
2. Aperte BOOT **em silêncio** e anote o `rms`. Com ganho 40x, espere de -58 a -45 dBFS. Acima
   de -35, suspeite de fiação, alimentação ou sala barulhenta. Aqui o aviso `audio muito baixo`
   é normal: **não** aumente o ganho por causa dele.
3. Aperte BOOT **com música** e anote o `rms`. O ideal com ganho 40x é perto de -21 dBFS
   (~70 dB SPL, pela sensibilidade típica do INMP441).
4. Subtraia o rms do silêncio do rms da música (medidos com o mesmo ganho):

   | Diferença | Expectativa, **por simulação** |
   |---|---|
   | 20 dB ou mais | ótimo |
   | 10 a 20 dB | deve servir com as cinco músicas |
   | menos de 10 dB | arriscado: aproxime a caixa ou reduza o barulho da sala |

5. Se `saturadas` passar de 1%, baixe o volume ou use `GANHO_MIC 10.0f` e regrave. Som alto
   distorce com 40x: na simulação a 90 dB SPL, o acerto foi de 74,5% (40x) e 99% (10x).

### O que mandar de volta para quem roda o servidor

- O texto do serial, principalmente as linhas `captura:` (silêncio e música) e `RESULTADO`.
- Três tentativas por música, dizendo qual tocava em cada uma.
- Uma foto do OLED, o volume e a distância da caixa.

## 6. Diagnóstico

| Sintoma no serial | Causa provável | O que fazer |
|---|---|---|
| `audio muito baixo` / pico abaixo de -30 dBFS **com música tocando** | ganho baixo ou caixa longe | aumente `GANHO_MIC` (ex.: 80) e regrave; aproxime a caixa |
| `muitas amostras saturadas` (> 1%) | ganho alto ou volume alto | baixe o volume ou use `GANHO_MIC 10.0f` e regrave |
| pico em `-120 dBFS` sempre, VU parado | microfone sem sinal | confira `SD=GPIO32`, `SCK=GPIO14`, `WS=GPIO15`, `L/R` no GND, 3,3 V |
| `Olaf: 0 fingerprints` com nível bom | áudio sem conteúdo musical (só ruído) | toque música, não fala |
| `match=0` com centenas de hashes recebidos e poucos acertados | eco, ruído, saturação ou música fora do catálogo | use as cinco músicas; aproxime a caixa; reduza o ruído; teste outra faixa |
| `HTTP -1` / `sem conexao` | sem internet no Wi-Fi ou URL antiga | confira o Wi-Fi; se o servidor reiniciou, `git pull` (servidor.h novo) e regrave |
| `HTTP 401` | `API_KEY` do `config.h` diferente da do servidor | peça a chave de novo (`api-key.secret`) e regrave |
| `HTTP 502`/`530` ou timeout | servidor desligado no notebook | avise quem roda o servidor |
| ESP não termina o boot com o microfone ligado | `WS` no GPIO15 (pino de strapping) | desconecte o fio do WS, grave/ligue, reconecte |

## 7. Plano B

- **Sem microfone funcionando:** grave com `FONTE_AUDIO_CLIPE 1`. O ESP processa um trecho de
  "Time Tellers" (Anitek) guardado na flash e reconhece pela mesma rede/servidor.
  Mostra o sistema inteiro menos a captura.
- **Sem internet na sala:** ligue o notebook no mesmo hotspot e use
  `./scripts/subir-servidor.ps1 -Tunnel:$false`. Ele mostra `http://<ip>:5184/match` para o
  `SERVIDOR_URL` do `servidor.h`. O firewall do Windows pode bloquear a porta 5184 (libere ou volte ao túnel).
- Testar o servidor sozinho, sem o ESP: `./scripts/testar-reconhecimento.ps1 -Count 10 -DurationSeconds 5`.

## 8. No dia da apresentação

- [ ] Notebook na tomada e com a **tampa aberta** (fechar suspende). Docker aberto.
- [ ] Se o notebook foi transportado, suspendeu ou trocou de rede, confira o `/health` público.
      Se não responder: `subir-servidor.ps1 -Publicar`, push e regravar o ESP **no local**.
- [ ] Hotspot do celular ligado em 2,4 GHz, com dados móveis.
- [ ] As cinco músicas abertas; uma testada no local antes de começar.
- [ ] Plano B ensaiado: trocar para `FONTE_AUDIO_CLIPE 1` e regravar leva ~1 min.
