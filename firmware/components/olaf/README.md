# Componente Olaf (ESP-IDF)

Placeholder. A lib [Olaf](https://github.com/JorenSix/Olaf) entra aqui como componente
ESP-IDF na **Fase 4**.

Plano:

1. Adicionar os fontes do Olaf necessários para extração de fingerprint neste diretório
   (ou como submódulo git), do **mesmo commit** usado na compilação da DLL em `native/`
   — isso garante hashes bit-a-bit idênticos entre firmware e ingestão (seção 3.2 e 11).
2. Criar um `CMakeLists.txt` com `idf_component_register(SRCS ... INCLUDE_DIRS ...)`.
3. Expor uma função de extração chamável pela `FingerprintTask` (ver `../../main/fingerprint.c`).

> Os fontes do Olaf não são versionados neste repo por enquanto (ver `.gitignore`).
