# Setup do Cloudflare Tunnel (Fase 6)

Expõe o backend (que roda em `localhost:5184`) numa URL pública `https://*.trycloudflare.com`,
mantendo o Valkey/SQLite em casa. Ver `architecture.md` seções 3.2 (decisão 5) e 8 (Fase 6).

> `cloudflared` ainda **não** está instalado nesta máquina.

## 1. Instalar cloudflared (Windows)

```powershell
winget install --id Cloudflare.cloudflared
# ou baixar o .exe de https://github.com/cloudflare/cloudflared/releases
```

## 2. Túnel rápido (sem conta — URL efêmera)

Bom para testes e demo pontual:

```powershell
# com a API rodando em http://localhost:5184
cloudflared tunnel --url http://localhost:5184
```

Isso imprime uma URL `https://<aleatório>.trycloudflare.com`. Copie-a para
`SERVER_URL` em `firmware/main/config.h` (acrescentando `/match`).

> URL efêmera muda a cada execução. Para demo, gere pouco antes e atualize o config.h.

## 3. Túnel nomeado (persistente — requer conta Cloudflare + domínio)

```powershell
cloudflared tunnel login
cloudflared tunnel create shazamzin
# criar config.yml apontando o hostname -> http://localhost:5184
cloudflared tunnel route dns shazamzin shazamzin.seu-dominio.com
cloudflared tunnel run shazamzin
```

## 4. Fallbacks para a apresentação (seção 11)

1. **Túnel Cloudflare** (padrão)
2. **Hotspot do celular** + IP fixo por reserva DHCP em LAN
3. **Vídeo de backup** gravado em casa

## Checklist Fase 6

- [ ] cloudflared instalado e túnel subindo
- [ ] HTTPS no ESP32 (`esp_http_client` com cert bundle)
- [ ] Testado via hotspot do celular acessando a URL pública
- [ ] URL pública atualizada em `config.h`
