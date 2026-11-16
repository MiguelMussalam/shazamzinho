# Shazamzinho

Reconhecimento de músicas com **ESP32 + Olaf + .NET**. O backend reconhece trechos
de áudio reais; o firmware do ESP32 já dá "sinal de vida" (captura pelo microfone e
mostra no OLED), e a ligação ESP32 → backend é a próxima fase.

![Montagem do ESP32 com microfone INMP441 e display OLED na protoboard](docs/fritzing/Shazamzin_bb.png)

[Arquivo do Fritzing](docs/fritzing/Shazamzin.fzz) · [Guia de montagem](docs/fritzing/README.md) · [Arquitetura](docs/architecture.md)

## O que funciona hoje

| Parte | Estado |
|---|---|
| **Backend** (API + ingestão) | Reconhece trechos reais de áudio via CLI/Scalar. |
| **Firmware** (sinal de vida) | Captura o microfone (I2S) e mostra status + IP + barra de VU no OLED. |
| **ESP32 → backend** | Ainda não — o módulo `network` do firmware é stub (fase seguinte). |

Ou seja: o reconhecimento é testado hoje pela **CLI/Scalar**, não pelo ESP32. Os dois
lados rodam de forma independente por enquanto.

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

---

## Compilar e gravar o firmware do zero (sinal de vida)

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
git checkout feat/sinal-de-vida-mic-oled
```

### 3. Credenciais (config.h)

```powershell
cp firmware/main/config.h.example firmware/main/config.h
```

Para o sinal de vida basta `WIFI_SSID` / `WIFI_PASSWORD` — e mesmo **sem Wi-Fi válido**
ele roda (mostra "sem Wi-Fi" e a barra de VU funciona igual).

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

OLED: "Conectando Wi-Fi..." por alguns segundos, depois `Shazamzin`, o IP (ou
"sem Wi-Fi") e `Ouvindo...`, com uma **barra de VU que sobe ao falar/bater palma**
perto do microfone. No serial (115200): logs `nivel=...`. Barra reagindo ao som =
mic + display + CPU funcionando juntos.

Ajustes de bancada (endereço I2C `0x3C`/`0x3D`, sensibilidade) e detalhes dos módulos:
[firmware/README.md](firmware/README.md).

---

## Ligar o ESP32 ao backend (próxima fase)

Quando o módulo `network` do firmware sair do stub, a API local é exposta por um
túnel para o ESP32 alcançá-la:

1. Suba a API (passos acima) e um túnel: `cloudflared tunnel --url http://localhost:5184`.
2. Copie a URL `https://<...>.trycloudflare.com/match` para `SERVER_URL` em `firmware/main/config.h`.

Guia completo e fallbacks de demo: [scripts/setup-cloudflared.md](scripts/setup-cloudflared.md).

---

## Testes

```powershell
dotnet test backend/Shazamzin.slnx
```

Para os testes de falha/reparo da ingestão, use um Valkey descartável — ver
[docs/ingestion.md](docs/ingestion.md#validação-e-limites-desta-entrega).
