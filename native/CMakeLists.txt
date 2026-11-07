# Compilação da lib Olaf como DLL/shared lib (Fase 3)
# Gera libolaf.dll expondo olaf_extract_fingerprints(...) para P/Invoke no Ingestion.
#
# Pré-requisitos: cmake + um compilador C (MSVC ou MinGW), Olaf clonado em native/olaf/.
# Ver native/build.ps1 e docs/architecture.md seção 3 (decisão 3) e 8 (Fase 3).

cmake_minimum_required(VERSION 3.16)
project(olaf_native C)

set(CMAKE_C_STANDARD 11)

# Caminho dos fontes do Olaf (clonados por build.ps1).
set(OLAF_DIR "${CMAKE_CURRENT_SOURCE_DIR}/olaf")

if(NOT EXISTS "${OLAF_DIR}/src")
    message(FATAL_ERROR
        "Fontes do Olaf nao encontrados em ${OLAF_DIR}. "
        "Rode native/build.ps1 (ele clona o Olaf) antes de configurar o CMake.")
endif()

# TODO Fase 3: ajustar a lista de fontes conforme a estrutura real do Olaf.
file(GLOB OLAF_SOURCES "${OLAF_DIR}/src/*.c")

# Wrapper que expõe uma API estável para P/Invoke (a ser escrito na Fase 3):
#   native/olaf_wrapper.c -> olaf_extract_fingerprints(wav_bytes, len, out_hashes, out_count)
list(APPEND OLAF_SOURCES "${CMAKE_CURRENT_SOURCE_DIR}/olaf_wrapper.c")

add_library(olaf SHARED ${OLAF_SOURCES})
target_include_directories(olaf PRIVATE "${OLAF_DIR}/src")

# Nome de saída: libolaf.dll (Windows) / libolaf.so (Linux)
set_target_properties(olaf PROPERTIES OUTPUT_NAME "olaf")
