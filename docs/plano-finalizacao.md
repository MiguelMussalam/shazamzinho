# Plano de finalização — Shazamzinho

**Data do levantamento:** 2026-11-20 · **Base:** `main` em `4993eb6` · **Equipe:** Miguel, João Pedro

Este plano parte do que foi **verificado** hoje (comandos rodados, placa testada), não só do que
os documentos dizem. As decisões da seção 3 ainda estão em aberto; até alguém decidir, o plano
assume a recomendação de cada uma.

---

## 0. Atualização — 2026-11-20 (tarde)

Com a placa **sem microfone e sem OLED**, o pipeline inteiro foi validado usando um trecho de
música gravado na flash (`FONTE_AUDIO_CLIPE = 1`). Branch `feat/pipeline-esp-sem-mic`.

- **M2 concluído:** Olaf roda no ESP32 e gera fingerprints **100% iguais** aos do PC
  (74/74 e 119/119 em dois trechos), ~720 ms por trecho de 5 s. O maior risco técnico do plano
  não se confirmou.
- **Ponta a ponta funcionando:** ESP no hotspot do celular → HTTPS pelo túnel Cloudflare → API
  → "Time Tellers — Anitek" reconhecida em **~3,6 s**. Heap estável entre ciclos.
- **Catálogo (D2/D3 decididos):** 586 músicas completas do MTG-Jamendo (`raw_30s_audio-low-00.tar`,
  CC, uso acadêmico), 251 mil chaves, 35 MB no Valkey (database 1). Ingestão em 20 min.
- **Taxa de acerto** (trechos aleatórios, desalinhados de propósito, 60 músicas): **3 s → 90%**,
  **5 s → 97%**, nenhuma música errada.
- Corrigidos: firmware travava sem OLED; placar dividia votos na borda dos buckets; CLI lia
  `10.6` como 106 em Windows pt-BR; limite de 1 MB do PlatformIO.
- Novos scripts: `subir-servidor.ps1` (Valkey + API em produção com chave aleatória + túnel),
  `ingest-folder.ps1` (pastas + metadados Jamendo), `gerar-clipe-teste.ps1`,
  `comparar-fingerprints-esp.ps1`, `testar-reconhecimento.ps1`.

**Ainda depende do hardware:** validar o microfone (M1) e o OLED. **Nova decisão (D6):** janela
de captura de 3 s (90%, resposta mais rápida) ou 5 s (97%, ~2 s a mais).

### Atualização — 2026-11-20 (noite)

Decisões: **apresentação na segunda, 23/11** (D1); **captura de 5 s** (D6); túnel rápido
mantido por enquanto (D4). Com isso o foco passou a ser deixar a `main` pronta para o teste
integrado de 21/11 ([`docs/teste-integrado.md`](teste-integrado.md)):

- Captura do microfone **em fluxo** (5 s não cabem na RAM como buffer), sobre uma API nova da
  ponte nativa, idêntica à extração do PC (testado no PC e na placa).
- Ganho do microfone configurável e com saturação (antes um som alto dava a volta e virava
  ruído), mais medição de nível em dBFS a cada captura.
- Modo microfone rodado na placa sem microfone: captura, consulta e volta ao ocioso sem travar.
- `match_log` gravado a cada consulta (histórico de reconhecimentos).
- Valkey manteve o catálogo após o Docker reiniciar (verificado).
- `subir-servidor.ps1` usa `127.0.0.1`: com `localhost` o PowerShell tentava IPv6 e não achava a API.
- Playlist do catálogo para o teste: `scripts/gerar-playlist.ps1`.

---

## 1. Estado atual (levantamento da manhã de 2026-11-20)

| Área | Situação | Evidência |
|---|---|---|
| API (`/match`, `/health`, `/catalog/fingerprints`, Scalar em `/docs`) | **Pronta** | 65 de 65 testes passam com Valkey + Valkey descartável de pé |
| Matching real (Valkey + SQLite, histograma de deltas) | **Pronto** | testes de integração; ~2 ms para 100 hashes (benchmark de 08/11) |
| Olaf nativo no PC (`libolaf.dll`, P/Invoke) | **Pronto** | DLL compilada; teste de equivalência C × .NET no mesmo PCM |
| Ingestão (CLI `add` de 1 arquivo, `extract`) | **Parcial** | reconheceu 2 músicas reais em 08/11; faltam pasta, paralelismo, `resume`, `remove`, `stats`, `verify` |
| Catálogo de músicas reais | **Inexistente** | o banco tem só as 3 músicas fake do seed; o Valkey de validação da Fase 3 está vazio (sem volume) |
| Firmware na `main` | **Fase 0** | boota e sobe o Wi-Fi na placa (COM8), mas só testado com SSID falso |
| Firmware "sinal de vida" (mic I²S + OLED) | **Fora da `main`, com bug** | branch `feat/sinal-de-vida-mic-oled`, 4 commits à frente. Compila; gravado na placa em 20/11: o OLED não respondeu no I²C e o firmware entrou em loop de reboot (ver problema 7) |
| Olaf no ESP32, rede (HTTP), máquina de estados com botão | **Não iniciado** | `fingerprint.c` e `network.c` são stubs |
| Túnel público, HTTPS no ESP | **Não iniciado** | `cloudflared` instalado, sem configuração |
| Montagem (Fritzing) | **Pronta** | `docs/fritzing/` corrigido em 09/11 |
| `match_log` (histórico de reconhecimentos) | **Não gravado** | tabela existe, nenhum código escreve nela |

### Problemas encontrados no levantamento

1. **Disco quase cheio (urgente):** C: tinha 3,2 GB livres no início do levantamento e **1,7 GB no
   fim**, só com builds e Docker rodando. D: tem 7,5 GB. Com pouco mais uso, builds e o Docker
   (Valkey grava o snapshot em C:) começam a falhar. Também não cabe um catálogo de milhares de MP3s.
2. **Não há biblioteca de músicas** nesta máquina (pasta Músicas vazia).
3. A API ainda usa a chave `dev-key-local`, versionada no `appsettings.json`. Não pode ir para o túnel público assim.
4. O database 0 do Valkey (o padrão da API) contém o catálogo fake do seed.
5. Branches velhas: `feature/fingerprint-ingestion-api` (já entrou via PR #1), `feature/fase-1-backend-fake`, `feature/fase-2-matching-real`.
6. `architecture.md` mostra as Fases 0–2 desmarcadas, embora estejam prontas.
7. **Firmware derruba a placa sem OLED.** Na branch do sinal de vida, `display_init()` usa
   `ESP_ERROR_CHECK` em `esp_lcd_panel_init` (`display.c`, linha 176): se o display não responde,
   chama `abort()` e o ESP reinicia sem parar. O `display.h` promete o contrário ("em caso de falha,
   as demais funções viram no-op"). Na placa testada (COM8) o OLED deu NACK no endereço 0x3C, então
   o display não está ligado ou a ligação está errada. A placa voltou para o firmware da `main`.
8. Espaço recuperável com segurança: ~1,8 GB do PlatformIO (ESP-IDF 6.1 e toolchain GCC 15, que não
   são usados desde que fixamos o 5.4.1) e 0,34 GB do Fritzing portátil. O disco do Docker ocupa 3,4 GB.
   Isso não basta para a meta de espaço do M0: o resto precisa sair de arquivos pessoais ou ir para o D:/HD externo.

---

## 2. Definição de "pronto"

O projeto está pronto quando, **na sala de apresentação**:

1. Alguém toca uma música do catálogo perto do microfone e aperta o botão BOOT.
2. Em até ~5 s o OLED mostra título e artista (ou "não reconhecida" quando for o caso).
3. Isso funciona com o servidor em casa, via túnel, e há dois planos B ensaiados:
   hotspot do celular em LAN e vídeo gravado.
4. O README explica como montar e rodar tudo do zero, e o catálogo pode ser reconstruído por script.

Tudo que não serve a esses quatro pontos é opcional (seção 7).

---

## 3. Decisões pendentes

Estas decisões mudam o tamanho do trabalho. Cada uma tem uma recomendação, que este plano assume
até alguém decidir diferente.

| # | Decisão | Recomendação (assumida no plano) | Por quê |
|---|---|---|---|
| D1 | **Data da apresentação** | ~13–17/jan/2027 (semanas 10–11 do plano original) | É o que a `architecture.md` previa. Com outra data, o cronograma da seção 6 muda |
| D2 | **Tamanho do catálogo** | **300–500 músicas** em vez de 5.000 | Não há biblioteca nem disco para 5k. Para a demo, o que importa é reconhecer bem as músicas tocadas, não o volume. Se depois houver HD externo, a mesma CLI escala |
| D3 | **De onde vêm as músicas** | Biblioteca pessoal num HD externo/pendrive. Os arquivos só precisam existir durante a ingestão | A ingestão guarda só fingerprints e metadados. O MP3 pode ser apagado depois |
| D4 | **URL pública** | Túnel **nomeado** do Cloudflare (URL fixa). Se não houver domínio, `SERVER_URL` configurável pelo serial e salvo na NVS | O túnel rápido muda de URL a cada execução, o que exigiria regravar o ESP antes de cada uso |
| D5 | **Divisão do trabalho** | Trilha A (firmware/hardware) e trilha B (backend/catálogo/infra) em paralelo | As duas só se encontram no M3 |
| D6 | **Janela de captura** | 5 s, se o tempo total couber na demo | 3 s acerta 90% e 5 s acerta 97% (medido). Com microfone e ruído a diferença tende a crescer |

D2 e D3 foram decididos em 20/11: catálogo de 586 músicas do MTG-Jamendo.

---

## 4. Marcos

Cada marco termina com algo que roda e é demonstrável. As trilhas A e B andam em paralelo até o M3.

### M0 — Arrumar a casa (trilha A+B, 1–2 dias)

A limpeza de disco vem antes de tudo: com menos de 2 GB livres, qualquer outro passo pode falhar no meio.

- [x] Na branch `feat/sinal-de-vida-mic-oled`, trocar o `abort()` do display por log de erro e
      seguir sem tela (problema 7). Só então integrar na `main` via PR (é fast-forward, sem conflito).
- [ ] Apagar as branches velhas da seção 1 (local e remoto).
- [x] Liberar disco: `framework-espidf` e `toolchain-xtensa-esp-elf` **sem** sufixo de versão
      (são os do ESP-IDF 6.1; os com `@3.50401.0` e `@14.2.0+20241119` ficam), plataforma
      `espressif32` 8.x, Fritzing portátil de dentro do repo, `docker builder prune` (~2,2 GB no total).
      O resto vem de arquivos pessoais ou de mover dados para o D:. **Meta: ≥ 15 GB livres em C:.**
- [x] Preencher `firmware/main/config.h` com o Wi-Fi real (2,4 GHz) e ver o IP no serial. Fecha a Fase 0.
- [ ] Separar os catálogos: database 0 limpo para o catálogo real; fake/testes em outros databases.
      Documentar o par oficial SQLite + Valkey no README.
- [ ] Marcar as Fases 0–2 como concluídas na `architecture.md`.

**Pronto quando:** `main` única e atualizada, disco com folga, ESP mostra o IP real.

### M1 — Hardware validado (trilha A)

- [ ] Montar (ou conferir) a placa conforme `docs/fritzing/README.md`. Antes de ligar, testar com
      multímetro a continuidade dos trilhos e a separação 3,3 V × GND.
- [ ] Varredura I²C no boot (logar os endereços que respondem) para diagnosticar o OLED: em 20/11
      ninguém respondeu em 0x3C.
- [ ] Gravar o firmware de sinal de vida: o OLED mostra status e IP, e a barra de VU reage a palmas e à fala.
- [ ] **Validação de áudio (crítica):** mandar o PCM do microfone para o PC (UDP ou serial) e ouvir
      com `ffplay`. Conferir que não há estalos, cortes, saturação nem silêncio. Ajustar o
      `DESLOCAMENTO_AMOSTRA` (hoje `>> 14`) para usar bem a faixa sem saturar.
- [ ] Confirmar que o ESP boota com o mic ligado ao GPIO15 (pino de strapping).

**Pronto quando:** existe uma gravação de 10 s do microfone no PC que soa limpa.

### M2 — Olaf no ESP32 (trilha A) — o marco de maior risco

- [x] Olaf como componente ESP-IDF em `firmware/components/olaf/`, **mesmo commit** de
      `native/olaf.version` e **mesma configuração** de `native/shazamzin_olaf_config.h`
      (8 kHz, bloco 1024, passo 128, `-ffp-contract=off`).
- [ ] Processamento contínuo: alimentar o Olaf bloco a bloco enquanto captura, sem guardar 3 s de
      áudio inteiros (economiza ~96 KB de RAM).
- [x] **Teste de consistência:** gravar na flash do ESP um trecho PCM fixo (é a "simulação com áudio
      bruto" combinada). Rodar o Olaf nele no ESP e no PC (`Shazamzin.Ingestion extract --raw-output`)
      e comparar os hashes.
      **Critério:** ≥ 90% dos hashes iguais **e** a API reconhece a música a partir dos hashes do ESP.
      Igualdade 100% não é exigida: Xtensa/newlib e x86/MinGW calculam floats de forma um pouco diferente.
- [ ] Monitorar o heap durante 10 min de execução contínua (não pode cair com o tempo).

**Pronto quando:** os hashes que o ESP gera a partir do trecho fixo reconhecem a música na API.

### M3 — Ponta a ponta na rede local (trilhas A+B juntas)

- [x] Firmware: `network.c` com `esp_http_client` (POST octet-stream, `X-Api-Key`, `X-Device-ID`),
      resposta lida com cJSON.
- [x] Máquina de estados completa: `IDLE → CAPTURANDO → PROCESSANDO → CONSULTANDO → RESULTADO → IDLE`,
      iniciada pelo botão BOOT (GPIO 0), com debounce. _O ciclo automático do boot foi testado;
      o clique no BOOT ainda precisa ser conferido por alguém na frente da placa._
- [ ] OLED mostrando cada estado, título e artista do resultado, "não reconhecida", e erro de rede com
      mensagem clara.
- [x] Backend: gravar cada consulta no `match_log` (device, música, confiança, hashes, tempo).
- [ ] Medir a latência fim a fim (botão → resultado na tela) e registrar no `sprint-log`.

**Pronto quando:** com o mic, 20 músicas diferentes tocadas por uma caixa de som, ≥ 80% reconhecidas
em ≤ 5 s, servidor no notebook na mesma rede.

### M4 — Catálogo final (trilha B, em paralelo desde o M0)

- [x] Ingestão de pasta recursiva, com nova tentativa automática por arquivo e metadados do
      Jamendo. _Feito em `scripts/ingest-folder.ps1`, sequencial (~1,5 s por música). Paralelismo
      dentro da CLI ficou de fora: não é necessário para 586 músicas._
- [ ] CLI: `resume`, `remove --song-id`, `stats` (músicas, hashes únicos, RAM do Valkey) e `verify`
      (SQLite × Valkey consistentes).
- [x] Barra de progresso e resumo final (ok / pulados / falhas, com motivo).
- [x] Ingerir o catálogo decidido em D2 no par oficial (`backend/data/catalogo.db` + database 1).
- [x] Conferir que o Valkey mantém o catálogo após reiniciar o Docker (volume nomeado + snapshot).
- [ ] Script de backup e restauração do par SQLite + `dump.rdb`.
- [ ] Teste de ruído: 30 consultas extraídas de músicas do catálogo, tocadas por caixa de som e
      capturadas pelo microfone do notebook, mais 10 de músicas fora do catálogo (precisam dar no-match).

**Pronto quando:** `stats` e `verify` limpos, catálogo sobrevive a reinício, taxa de acerto medida.

### M5 — Acesso remoto e robustez (trilhas A+B)

- [ ] Túnel Cloudflare conforme D4, subindo com um script junto da API.
- [ ] HTTPS no ESP (`esp_http_client` com o bundle de certificados do ESP-IDF). Medir o heap com TLS.
- [x] Chave real de `/match`: `subir-servidor.ps1` gera uma aleatória (`api-key.secret`, fora do git)
      e sobe a API em Production (sem `/docs`). `Ingestion__ApiKey` não é configurada, então o
      cadastro pela API fica desligado (responde 503) quando exposto pelo túnel.
- [ ] Teste de estresse: 50 consultas seguidas. O heap do ESP e a memória da API não podem crescer.
- [x] Testar o ESP no hotspot do celular (4G) acessando a URL pública.
- [ ] Notebook servidor sem suspensão durante a demo; API, Valkey e túnel sobem com um só comando.

**Pronto quando:** reconhecimento funcionando com o ESP no 4G do celular e o servidor em casa.

### M6 — Apresentação (trilhas A+B)

- [ ] Roteiro com tempo marcado: o que é, diagrama (Fritzing + arquitetura), demo ao vivo e perguntas.
- [ ] Fallbacks ensaiados: túnel → hotspot com servidor em LAN → vídeo.
- [ ] Vídeo de backup: 3 reconhecimentos, OLED visível e serial na tela.
- [ ] Lista de músicas "seguras" para a demo (as com melhor taxa no teste do M4).
- [ ] README final: montagem, gravação do firmware, subir o servidor, ingerir o catálogo.
- [ ] Ensaio completo pelo menos 2 vezes, uma delas fora de casa.

**Pronto quando:** ensaio fora de casa concluído sem intervenção manual no código.

---

## 5. Riscos

| Risco | Chance | Impacto | Mitigação |
|---|---|---|---|
| Hashes do ESP não batem o suficiente com os do PC (float/libm diferentes) | Média | Alto | Critério de ≥ 90% em vez de 100%. Se não fechar, testar `-ffp-contract=off` e tabelas de seno/janela pré-calculadas no componente. Último recurso: ESP manda PCM e o servidor extrai (perde o "fingerprint no dispositivo") |
| RAM do ESP32 clássico com Wi-Fi + TLS + Olaf | Média | Alto | Processamento contínuo, sem buffer de 3 s; medir o heap em cada marco; HTTPS só no M5 |
| Microfone com ruído, ganho baixo ou estalos | Média | Alto | Validação de áudio do M1 antes de qualquer fingerprint |
| Disco cheio impede o catálogo e o Docker | **Alta** (já acontece) | Alto | Limpeza do M0; catálogo de 300–500 músicas; MP3s fora do disco |
| Sala sem rede ou bloqueio do túnel | Média | Alto | Hotspot + servidor em LAN; vídeo de backup |
| URL do túnel muda e exige regravar o ESP | Alta com túnel rápido | Médio | Decisão D4 |
| Falta de tempo com as festas de fim de ano | Alta | Médio | M2 (o mais arriscado) começa já; M4 anda em paralelo; seção 7 só com folga |
| Valkey perde o catálogo ao reiniciar | Baixa | Médio | Volume nomeado + teste de reinício + backup no M4 |

---

## 6. Cronograma sugerido

Assume apresentação por volta de 13–17/jan/2027 (D1). Datas são semanas, não prazos rígidos.

| Semana | Trilha A — firmware/hardware | Trilha B — backend/catálogo |
|---|---|---|
| 20–23/nov | M0 | M0 |
| 24–30/nov | M1 (áudio validado) | M4: CLI de pasta, `stats`, `verify`, `resume`, `remove` |
| 1–14/dez | M2 (Olaf no ESP, consistência) | M4: ingestão do catálogo, teste de ruído; `match_log` |
| 15–21/dez | M3 (ponta a ponta em LAN) | M3 junto com a trilha A |
| 22/dez–4/jan | folga / atraso acumulado | folga / atraso acumulado |
| 5–9/jan | M5 (HTTPS, estresse, hotspot) | M5 (túnel, chaves, um comando para subir tudo) |
| 12/jan → | M6 (vídeo, ensaios) | M6 (README, roteiro) |

O caminho crítico é **M1 → M2 → M3**, todo na trilha A. Se o M2 atrasar mais de uma semana,
acionar a mitigação "ESP manda PCM" do primeiro risco, para não comprometer a demo.

---

## 7. Opcional (só com folga depois do M6)

- LED RGB de status.
- Dashboard web com o `match_log` e as estatísticas do catálogo.
- WebSocket no lugar de HTTP.
- Case impresso em 3D.
- Catálogo maior (5k+) com HD externo.
