#pragma once
// Endereço do servidor que o ESP32 consulta. VERSIONADO de propósito: quem só grava o
// firmware (sem rodar o servidor) faz `git pull` e já pega o endereço atual.
// Atualizado por:  ./scripts/subir-servidor.ps1 -Publicar
//
// O túnel rápido do Cloudflare muda de URL a cada reinício do servidor: depois de um
// reinício, publique de novo, faça pull e regrave o ESP.
//
// A CHAVE da API não fica aqui (o repositório é público): ela vai em API_KEY no config.h,
// que é ignorado pelo git. Peça a chave a quem roda o servidor; ela não muda entre reinícios.

#define SERVIDOR_URL      "https://server-robot-passenger-officials.trycloudflare.com/match"
