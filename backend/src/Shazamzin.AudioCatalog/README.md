# Catálogo de áudio

Esta CLI cadastra arquivos e identifica trechos pelas assinaturas do som. O SQLite guarda o catálogo completo. O Valkey é opcional e mantém uma cópia das assinaturas para buscas rápidas. A primeira busca com o índice vazio o reconstrói a partir do SQLite. Se o Valkey estiver fora do ar, o cadastro e a identificação continuam pelo SQLite.

## Rodar localmente

Use o SDK .NET 10. Para MP3, WebM, Opus, Ogg ou FLAC, coloque o FFmpeg no PATH. Para WAV PCM, a CLI faz a leitura diretamente.

Da raiz do repositório, suba o Valkey com Docker Compose e configure o endereço na sessão PowerShell:

~~~powershell
docker compose -f backend/src/Shazamzin.AudioCatalog/docker-compose.yml up -d
$env:Valkey__ConnectionString = "127.0.0.1:6382"
~~~

O Compose usa a porta local 6382 por padrão. Para trocar essa porta, defina `AUDIO_CATALOG_VALKEY_PORT` antes de subir o serviço e ajuste o endereço na CLI. Ao usar um Valkey existente, configure a política `noeviction` para que o servidor preserve todas as chaves de um índice publicado.

Cadastre uma faixa inteira e procure um trecho de pelo menos três segundos:

~~~powershell
$project = "backend/src/Shazamzin.AudioCatalog"
dotnet run --project $project -- register "track.wav" --title "Example Track" --artist "Example Artist" --database "audio-catalog.db"
dotnet run --project $project -- identify "excerpt.wav" --database "audio-catalog.db"
~~~

O comando de identificação imprime o título, artista, pontuação e posição em segundos, ou `null` se não encontrar uma correspondência. O resultado em JSON sai pela saída padrão e os logs saem pela saída de erros.

| Configuração | Para que serve |
|---|---|
| `ConnectionStrings__AudioCatalog` | Conexão SQLite, por exemplo `Data Source=audio-catalog.db`. A opção `--database` tem prioridade. |
| `Valkey__ConnectionString` | Conexão opcional com o Valkey. Deixe vazia para usar somente o SQLite. |
| `Identification__MinScore` | Mínimo de votos alinhados para aceitar uma faixa, com padrão 20. |
| `Identification__DeltaBucketMs` | Tamanho, em milissegundos, das faixas de tempo usadas na votação, com padrão 50. |

Os mesmos valores podem ser configurados no `appsettings.json` da CLI. Para carregar PCM bruto de 8 kHz produzido pela CLI anterior, passe `--format f32le-8000-mono`.

## Testes

~~~powershell
dotnet test backend/tests/Shazamzin.AudioFingerprinting.Tests/Shazamzin.AudioFingerprinting.Tests.csproj
$env:AUDIO_CATALOG_TEST_VALKEY = $env:Valkey__ConnectionString
dotnet test backend/tests/Shazamzin.AudioCatalog.Tests/Shazamzin.AudioCatalog.Tests.csproj
~~~

Com `AUDIO_CATALOG_TEST_VALKEY` configurada, o teste de integração também usa o Valkey. Cada catálogo temporário tem um prefixo próprio no índice.
