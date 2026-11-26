# Shazamzinho

Reconhecimento de músicas com ESP32. O ESP32 capta o som por um microfone INMP441, gera as
impressões digitais do áudio (fingerprints) com o Olaf no próprio chip e manda para uma API
em .NET, que procura a música num catálogo e devolve título e artista para o display OLED.

![Montagem na protoboard](docs/fritzing/Shazamzin_bb.png)

Montagem e pinagem: [docs/fritzing/README.md](docs/fritzing/README.md)

## Integrantes

- Miguel Mussalam Silva (RA 1123299)
- Thiago Danilow de Araujo
- Pedro de França Pereira (RA 8165880)
- João Pedro Thomaz Kairalla dos Santos (RA 6898366)
- Joaquim Luiz Malacarne Lima de Oliveira (RA 4418635)

Relatório da atividade prática: [docs/entrega/relatorio-shazamzinho.docx](docs/entrega/relatorio-shazamzinho.docx)

## Arquivos principais

O código que roda no hardware fica na pasta [firmware](firmware/):

- [firmware/main/main.c](firmware/main/main.c): conecta no Wi-Fi e inicia o resto
- [firmware/main/maquina_de_estado](firmware/main/maquina_de_estado/): controla o ciclo ouvir, processar, consultar e mostrar o resultado
- [firmware/main/captura_de_audio](firmware/main/captura_de_audio/): leitura do microfone por I2S
- [firmware/main/fingerprint](firmware/main/fingerprint/): gera os fingerprints com o Olaf
- [firmware/main/network](firmware/main/network/) e [protocolo](firmware/main/protocolo/): envio para a API (`POST /match`)
- [firmware/main/resposta](firmware/main/resposta/): lê a resposta da API
- [firmware/main/display](firmware/main/display/): driver do OLED SSD1306 (I2C)

O servidor de reconhecimento está em [backend](backend/) e [native](native/).

## Estado atual

- O backend tem um catálogo de 586 músicas e acerta 97 a 99% dos trechos de 5 s com áudio limpo.
- O Olaf no ESP32 gera os mesmos fingerprints que o PC.
- O ciclo ESP32 → servidor → resultado leva uns 3,6 s.
- Falta testar com o microfone e o OLED montados. Roteiro em [docs/teste-integrado.md](docs/teste-integrado.md).

## Firmware

Precisa do VS Code com a extensão PlatformIO e do driver USB da placa (CP210x ou CH340).
O caminho da pasta do projeto não pode ter espaços, senão o ESP-IDF não compila.

```powershell
git clone https://github.com/MiguelMussalam/shazamzinho.git
cd shazamzinho/firmware
cp main/config.h.example main/config.h   # Wi-Fi 2,4 GHz e API_KEY
pio run -t upload -t monitor
```

A primeira compilação baixa o ESP-IDF (uns 1 GB) e demora. Se travar em "Connecting...",
segure o botão BOOT da placa.

O endereço do servidor fica em `firmware/main/servidor.h`. Com `FONTE_AUDIO_CLIPE 1` no
`config.h` o firmware usa um trecho de música gravado na flash em vez do microfone.

Funcionando, o OLED mostra o IP e uma barra que sobe com o som. Um reconhecimento roda no
boot e depois a cada clique no botão BOOT (ouve 5 s e mostra título e artista). Mais
detalhes em [firmware/README.md](firmware/README.md).

## Backend

Precisa de .NET 10, Docker, FFmpeg, CMake e um compilador C (testado com MinGW).

```powershell
./native/build.ps1
dotnet build backend/Shazamzin.slnx
./scripts/setup-valkey.ps1

# indexar uma música
dotnet run --project backend/src/Shazamzin.Ingestion -- add `
  --path "C:\Musicas\faixa.mp3" `
  --sqlite "Data Source=./data/catalog-real.db" `
  --valkey "localhost:6379,defaultDatabase=1"

# subir a API
dotnet run --project backend/src/Shazamzin.Api -- `
  --Sqlite:ConnectionString="Data Source=./data/catalog-real.db" `
  --Valkey:ConnectionString="localhost:6379,defaultDatabase=1"
```

A API fica em http://localhost:5184 (documentação em `/docs`). Para testar com um trecho:

```powershell
dotnet run --project backend/src/Shazamzin.Ingestion -- extract `
  --path "C:\Musicas\faixa.mp3" --offset-seconds 10 --duration-seconds 3 `
  --output "$env:TEMP\consulta.bin"

./scripts/send-test-payload.ps1 -File "$env:TEMP\consulta.bin" -ApiKey "sua-chave"
```

Para o catálogo completo (586 músicas do MTG-Jamendo) e o servidor com túnel público, ver
`scripts/ingest-folder.ps1`, `scripts/subir-servidor.ps1` e o `docs/sprint-log.md` de 2026-11-20.

Testes: `dotnet test backend/Shazamzin.slnx`. Mais detalhes em [docs/ingestion.md](docs/ingestion.md).
