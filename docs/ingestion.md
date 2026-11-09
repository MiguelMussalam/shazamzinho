# Ingestão real — primeiro marco da Fase 3

Já é possível indexar **um arquivo por comando** e reconhecer trechos decodificados
independentemente pela API. A CLI ainda não implementa pastas, paralelismo,
`resume`, `remove`, `stats` ou `verify`.

## Preparação

No Windows, instale .NET 10, Git, CMake, FFmpeg (inclui FFprobe) e um compilador C.
O script usa GCC/MinGW e Ninja quando disponíveis; também procura o Ninja instalado
pelo PlatformIO. Sem GCC, usa o gerador padrão do CMake, que requer uma toolchain C
compatível. Os testes desta entrega foram feitos com GCC/MinGW x64.

A partir da raiz do repositório:

```powershell
./native/build.ps1
dotnet build backend/Shazamzin.slnx
```

O script busca o commit fixado em `native/olaf.version`. O build .NET copia
`libolaf.dll` para os diretórios de execução e publicação. Após recompilar a parte
nativa, rode novamente `dotnet build`.

## Catálogo separado para áudio real

O SQLite e o Valkey precisam formar um par exclusivo. O catálogo de fingerprints
simulados da Fase 2 não recebe áudio real automaticamente: a CLI rejeita catálogos
sem perfil ou com um perfil diferente. Os comandos abaixo usam um SQLite novo e
o database 1 do Valkey; escolha outro database **vazio** se esse já estiver em uso.

```powershell
# Valkey precisa estar disponível (Docker Desktop aberto).
./scripts/setup-valkey.ps1

dotnet run --project backend/src/Shazamzin.Ingestion -- add `
  --path "C:\Musicas\faixa.mp3" `
  --sqlite "Data Source=./data/catalog-real.db" `
  --valkey "localhost:6379,defaultDatabase=1"
```

Título, artista, álbum e duração vêm do FFprobe. Sem tags, o título usa o nome do
arquivo e o artista vira `Artista desconhecido`. É possível fornecer `--title`,
`--artist` e `--album`. A saída JSON informa `song_id`, `fingerprints`, `skipped`
e `profile`; progresso e erros vão para stderr. O código de saída é 0 em caso de
sucesso, 1 para erros e 130 para cancelamento.

As opções de conexão também aceitam `Sqlite__ConnectionString` e
`Valkey__ConnectionString` como variáveis de ambiente. Opções explícitas prevalecem.
O caminho SQLite relativo é ancorado na pasta `backend`, como na API. A CLI não lê
o `appsettings.json` da API: ao personalizar as conexões, passe os mesmos valores
às duas aplicações.

## Consulta de um trecho real

Inicie a API apontando para o mesmo par:

```powershell
dotnet run --project backend/src/Shazamzin.Api -- `
  --Sqlite:ConnectionString="Data Source=./data/catalog-real.db" `
  --Valkey:ConnectionString="localhost:6379,defaultDatabase=1"
```

Em outro terminal, na raiz do repositório:

```powershell
dotnet run --project backend/src/Shazamzin.Ingestion -- extract `
  --path "C:\Musicas\faixa.mp3" --offset-seconds 10 --duration-seconds 3 `
  --output "$env:TEMP\consulta-10s.bin"
```

Esse comando decodifica o trecho e executa o Olaf novamente; não recorta hashes do
catálogo. Os timestamps são relativos ao início do trecho. A saída é um payload
v1 pronto para `/match`, com session ID 1 por padrão (ajustável por `--session-id`).
Arquivos de saída existentes não são sobrescritos. Há um limite de 65.535
fingerprints por consulta; use trechos curtos. O PCM decodificado tem limite de
256 MiB por arquivo nesta primeira versão.

Abra [Scalar](http://localhost:5184/docs), preencha a autenticação `ApiKey` com o
valor de `Auth:ApiKey` e envie o `.bin` como `application/octet-stream`. Ou use:

```powershell
./scripts/send-test-payload.ps1 -File "$env:TEMP\consulta-10s.bin" -ApiKey "sua-chave"
```

## Falhas, compatibilidade e retomada

- SHA256 do arquivo garante idempotência. Repetir um `add` concluído retorna
  `skipped: true`. O processamento de áudio ainda é refeito antes dessa checagem.
- Um lock local impede duas ingestões simultâneas no mesmo arquivo SQLite.
- SQLite e Valkey guardam a identidade do catálogo, incluindo perfil e revisão
  Olaf. Não troque apenas um dos bancos nem misture perfis no mesmo índice.
- O ID da música é reservado antes de escrever postings. Enquanto houver um log
  `pending` ou `failed`, a música não é retornada pelo repositório nem contada como
  pronta no `/health`.
- Se um write falhar, repita `add` para o mesmo arquivo após resolver a causa.
  A escrita Lua substitui apenas os postings daquele song ID em cada hash,
  preservando outras músicas e evitando duplicatas numa repetição parcial.
- Não há transação distribuída entre os bancos. Uma interrupção pode deixar
  postings parciais e log `pending`; repetir o arquivo repara esse estado.

## Validação e limites desta entrega

```powershell
# Testes existentes + testes nativos. Valkey padrão usa DB 15 para os testes antigos.
dotnet test backend/Shazamzin.slnx
```

Para os testes de falha/reparo da ingestão, use um Valkey **descartável**, separado
do catálogo. A variável abaixo permite que os testes esvaziem o DB indicado antes
e depois de cada caso. Databases 0 e 15 são rejeitados por esses testes novos.

```powershell
docker run -d --name shazamzin-ingestion-tests -p 127.0.0.1:6381:6379 valkey/valkey:8
$env:SHAZAMZIN_TEST_VALKEY = 'localhost:6381,defaultDatabase=14'
dotnet test backend/Shazamzin.slnx
```

Os testes comparam os fingerprints do P/Invoke com a CLI C da ponte no mesmo PCM,
verificam silêncio, amostras inválidas, sessões independentes, idempotência,
falha parcial, reparo e rejeição de catálogos incompatíveis. Não substituem a
comparação bit a bit com o ESP32 físico, prevista para a próxima fase.

Validação manual: duas músicas do dataset de testes usado pelo Olaf, cinco cortes
de 3 s nos offsets 10/25/34/50/75 da primeira música, uma consulta MP3 independente
e uma segunda música consultada antes e depois da indexação. Esse catálogo pequeno
não valida ainda ruído ambiente, captura por microfone ou escala de milhares de músicas.
