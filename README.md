# Shazamzinho

Reconhecimento de músicas com **ESP32 + Olaf + .NET**. O ESP32 capta o áudio, gera os
fingerprints com o Olaf no próprio chip e consulta a API, que compara contra um catálogo
no Valkey + SQLite e devolve título e artista para o OLED.

![Montagem do ESP32 com microfone INMP441 e display OLED na protoboard](docs/fritzing/Shazamzin_bb.png)

[Arquivo do Fritzing](docs/fritzing/Shazamzin.fzz) · [Guia de montagem](docs/fritzing/README.md) · [Arquitetura](docs/architecture.md)

## O que funciona hoje (2026-11-20)

| Parte | Estado |
|---|---|
| **Backend** (API + ingestão) | Catálogo de 586 músicas (MTG-Jamendo). Trechos de 5 s com áudio limpo: 97–99% de acerto. Com eco e ruído **simulados** cai (44% no pior cenário testado) e pode, raramente, responder a música errada. Microfone real ainda não medido: ver [teste integrado](docs/teste-integrado.md). |
| **Olaf no ESP32** | Fingerprints 100% iguais aos do PC no mesmo áudio, em ~0,7 s por 5 s de áudio. |
| **ESP32 → backend** | Ciclo completo pelo hotspot + túnel Cloudflare: ~3,6 s até o resultado. |
| **Microfone e OLED físicos** | Firmware pronto; falta o teste com o hardware montado. |

**Para testar tudo junto, siga [docs/teste-integrado.md](docs/teste-integrado.md).**

---

## Pré-requisitos

**Backend** (Windows): [.NET 10 SDK](https://dotnet.microsoft.com/download), Docker Desktop,
[FFmpeg](https://ffmpeg.org/) (inclui `ffprobe`), CMake, um compilador C (GCC/MinGW x64 testado), Git.

**Firmware:** VS Code + extensão **PlatformIO IDE** (baixa o toolchain ESP-IDF 5.4 na 1ª build)
e o driver USB-serial da placa (CP210x ou CH340).

**Opcional** (ligar ESP32 à API numa demo): [cloudflared](scripts/setup-cloudflared.md).

---

## Rodar o backend do zero

A partir da raiz do repositório (PowerShell):

```powershell
# 1. Compilar a ponte nativa do Olaf (libolaf.dll) + a solução .NET
./native/build.ps1
dotnet build backend/Shazamzin.slnx

# 2. Subir o Valkey (Docker Desktop precisa estar aberto)
./scripts/setup-valkey.ps1

# 3. Indexar uma música num catálogo real (SQLite novo + Valkey DB 1)
dotnet run --project backend/src/Shazamzin.Ingestion -- add `
  --path "C:\Musicas\faixa.mp3" `
  --sqlite "Data Source=./data/catalog-real.db" `
  --valkey "localhost:6379,defaultDatabase=1"

# 4. Rodar a API apontando para o MESMO par SQLite/Valkey
dotnet run --project backend/src/Shazamzin.Api -- `
  --Sqlite:ConnectionString="Data Source=./data/catalog-real.db" `
  --Valkey:ConnectionString="localhost:6379,defaultDatabase=1"
```

A API sobe em `http://localhost:5184` — abra o [Scalar (docs interativa)](http://localhost:5184/docs).

**Testar o reconhecimento** (noutro terminal): extraia um trecho e envie ao `/match`:

```powershell
dotnet run --project backend/src/Shazamzin.Ingestion -- extract `
  --path "C:\Musicas\faixa.mp3" --offset-seconds 10 --duration-seconds 3 `
  --output "$env:TEMP\consulta-10s.bin"

./scripts/send-test-payload.ps1 -File "$env:TEMP\consulta-10s.bin" -ApiKey "sua-chave"
```

Detalhes (tags, idempotência, cadastro via `POST /catalog/fingerprints`, limites):
[docs/ingestion.md](docs/ingestion.md).

### Catálogo de 586 músicas e servidor completo

O catálogo usado nos testes vem do [MTG-Jamendo](https://github.com/MTG/mtg-jamendo-dataset)
(músicas completas em Creative Commons, uso acadêmico): o arquivo `raw_30s_audio-low-00.tar`
(1,66 GB, 586 faixas), baixado e extraído em `C:\shazamzinho-dados\jamendo` — ver o
`docs/sprint-log.md` de 2026-11-20 para o comando com verificação SHA-256.

```powershell
# indexa a pasta inteira (~20 min); repetir é seguro
./scripts/ingest-folder.ps1 -Path C:\shazamzinho-dados\jamendo\audio `
  -JamendoMeta C:\shazamzinho-dados\jamendo\raw.meta.tsv `
  -Sqlite "Data Source=./data/catalogo.db" -Valkey "localhost:6379,defaultDatabase=1"

# Valkey + API (produção, chave aleatória em api-key.secret) + túnel público
./scripts/subir-servidor.ps1

# taxa de acerto com trechos aleatórios do catálogo
./scripts/testar-reconhecimento.ps1 -Count 30 -DurationSeconds 5
```

---

## Compilar e gravar o firmware do zero

Tutorial completo para compilar e gravar o firmware **numa máquina nova** (de
preferência com cabo de rede — a 1ª build baixa ~1 GB de toolchain).

Hardware: ESP32 DevKit V1 + microfone INMP441 + OLED SSD1306, montados conforme o
[guia de montagem](docs/fritzing/README.md) (pinagem lá).

### 1. Instalar o PlatformIO + driver USB

- **VS Code:** instale a extensão **PlatformIO IDE** (já traz o comando `pio`); ou
- **Linha de comando** (requer Python 3): `pip install platformio`.

Instale também o **driver USB-serial** da placa (**CP210x** ou **CH340**, conforme o
chip), senão a porta COM não aparece.

### 2. Pegar o código desta branch

> ⚠️ **O caminho NÃO pode ter espaços.** O ESP-IDF falha com
> `Detected a whitespace character in project paths`. Clone em algo como
> `C:\dev\` ou `C:\`, nunca numa pasta com espaço no nome (ex.: `Meus Projetos`).

```powershell
cd C:\dev        # uma pasta sem espaços no caminho
git clone https://github.com/MiguelMussalam/shazamzinho.git
cd shazamzinho
# Só quem roda o servidor precisa de ./native/build.ps1 (CMake + GCC). Para o firmware,
# a primeira build baixa sozinha o Olaf no commit fixado (só git e internet).
```

### 3. Credenciais (config.h)

```powershell
cp firmware/main/config.h.example firmware/main/config.h
```

Preencha `WIFI_SSID` / `WIFI_PASSWORD` (rede de **2,4 GHz**) e `API_KEY` (peça a quem roda o
servidor; ela não vai para o repositório). O endereço do servidor vem de
`firmware/main/servidor.h`, versionado e atualizado por quem roda o servidor
(`./scripts/subir-servidor.ps1 -Publicar`): basta `git pull`. `FONTE_AUDIO_CLIPE 0` usa o
microfone; `1` usa um trecho de música gravado na flash (testa tudo sem microfone).
Sem Wi-Fi válido o firmware ainda roda: gera os fingerprints e mostra "sem Wi-Fi".

### 4. Compilar

```powershell
cd firmware
pio run
```

> ⚠️ **Primeira build:** o PlatformIO baixa o toolchain ESP-IDF 5.4 (~1 GB) e as
> dependências — pode levar **10–30 min** dependendo da conexão (por isso o cabo de
> rede ajuda). É custo único da máquina; as próximas builds levam ~1–2 min.

### 5. Gravar na placa + abrir o serial

```powershell
pio run -t upload -t monitor
```

- Mais de uma porta? `pio device list` e depois `pio run -t upload -t monitor --upload-port COMx`.
- Travou em "Connecting..."? Segure o botão **BOOT** da placa durante a conexão.

### 6. O que esperar

OLED: "Conectando Wi-Fi...", depois `Shazamzin`, o IP e uma **barra de VU que sobe ao
falar/bater palma** perto do microfone. Um primeiro reconhecimento roda sozinho no boot;
depois, cada clique no botão **BOOT** ouve 5 s e consulta o servidor. O OLED mostra título
e artista (ou "Nao reconhecida"), e o serial (115200) mostra a linha `RESULTADO ...` e o
nível do áudio de cada captura. Sem OLED ligado, o firmware segue só com o serial.

Roteiro completo, diagnóstico e plano B: [docs/teste-integrado.md](docs/teste-integrado.md).
Detalhes dos módulos: [firmware/README.md](firmware/README.md).

---

## Testes

```powershell
dotnet test backend/Shazamzin.slnx
```

Para os testes de falha/reparo da ingestão, use um Valkey descartável — ver
[docs/ingestion.md](docs/ingestion.md#validação-e-limites-desta-entrega).
