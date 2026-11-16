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

## Rodar o firmware do zero (sinal de vida)

Hardware: ESP32 DevKit V1 + microfone INMP441 + OLED SSD1306, montados conforme o
[guia de montagem](docs/fritzing/README.md) (pinagem lá).

```powershell
# 1. Credenciais (config.h é gitignored — só o Wi-Fi importa nesta fase)
cp firmware/main/config.h.example firmware/main/config.h   # preencha WIFI_SSID / WIFI_PASSWORD

# 2. Compilar, gravar e abrir o monitor serial (ESP32 ligado no USB)
cd firmware
pio run -t upload -t monitor
```

**O que esperar:** o OLED mostra `Shazamzin`, o IP (ou "sem Wi-Fi") e `Ouvindo...`, com
uma **barra de VU que sobe ao falar/bater palma** perto do microfone. No serial, logs
`nivel=...`. Isso prova mic + display + CPU funcionando juntos.

Ajustes de bancada e detalhes dos módulos: [firmware/README.md](firmware/README.md).

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
