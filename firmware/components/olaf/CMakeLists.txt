# Olaf (extração de fingerprints) como componente ESP-IDF.
#
# Compila exatamente os mesmos fontes da libolaf.dll do PC (native/CMakeLists.txt):
# o Olaf clonado por native/build.ps1 no commit de native/olaf.version e a ponte
# native/olaf_wrapper.c com o perfil de native/shazamzin_olaf_config.h.
# Mesmo código + mesma configuração => fingerprints comparáveis com os do catálogo.

set(SZ_NATIVE "${CMAKE_CURRENT_LIST_DIR}/../../../native")
get_filename_component(SZ_NATIVE "${SZ_NATIVE}" ABSOLUTE)
set(SZ_OLAF "${SZ_NATIVE}/olaf")

file(STRINGS "${SZ_NATIVE}/olaf.version" SZ_OLAF_REVISION LIMIT_COUNT 1)

# Sem o Olaf clonado (máquina que só compila o firmware), baixa o commit fixado: ~6 MB,
# só git. Equivale ao clone do native/build.ps1, sem precisar de CMake/GCC para a DLL.
if(NOT EXISTS "${SZ_OLAF}/src/olaf_fft.c")
    message(STATUS "Olaf ausente: baixando ${SZ_OLAF_REVISION} em ${SZ_OLAF}")
    file(MAKE_DIRECTORY "${SZ_OLAF}")
    foreach(sz_cmd
            "init;-q"
            "remote;add;origin;https://github.com/JorenSix/Olaf.git"
            "fetch;-q;--depth;1;origin;${SZ_OLAF_REVISION}"
            "checkout;-q;--detach;FETCH_HEAD")
        execute_process(COMMAND git -C "${SZ_OLAF}" ${sz_cmd} RESULT_VARIABLE sz_rc)
        if(NOT sz_rc EQUAL 0)
            message(FATAL_ERROR "Falha ao baixar o Olaf (git ${sz_cmd}). Confira a internet e o git, ou rode native/build.ps1.")
        endif()
    endforeach()
endif()
execute_process(COMMAND git -C "${SZ_OLAF}" rev-parse HEAD
    OUTPUT_VARIABLE SZ_OLAF_ACTUAL OUTPUT_STRIP_TRAILING_WHITESPACE)
if(NOT SZ_OLAF_ACTUAL STREQUAL SZ_OLAF_REVISION)
    message(FATAL_ERROR "Olaf em ${SZ_OLAF_ACTUAL}, esperado ${SZ_OLAF_REVISION} (native/olaf.version). Rode native/build.ps1.")
endif()

idf_component_register(
    SRCS
        "${SZ_NATIVE}/olaf_wrapper.c"
        "${SZ_OLAF}/src/olaf_config.c"
        "${SZ_OLAF}/src/olaf_fft.c"
        "${SZ_OLAF}/src/olaf_ep_extractor.c"
        "${SZ_OLAF}/src/olaf_fp_extractor.c"
        "${SZ_OLAF}/src/olaf_max_filter_perceptual_van_herk.c"
        "${SZ_OLAF}/src/pffft.c"
    INCLUDE_DIRS "${SZ_NATIVE}"
    PRIV_INCLUDE_DIRS "${SZ_OLAF}/src"
)

# Mesmas definições e flags da DLL: PFFFT escalar e sem contração de FMA
# (o Xtensa tem madd.s; contrair mudaria o arredondamento em relação ao PC).
target_compile_definitions(${COMPONENT_LIB} PRIVATE
    PFFFT_SIMD_DISABLE
    SHAZAMZIN_OLAF_BUILD
    OLAF_REVISION="${SZ_OLAF_REVISION}")
target_compile_options(${COMPONENT_LIB} PRIVATE -ffp-contract=off -Wno-unused-variable -Wno-unused-function)
