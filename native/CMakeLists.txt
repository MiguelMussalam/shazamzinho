cmake_minimum_required(VERSION 3.19)
project(shazamzin_olaf C)
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_EXTENSIONS ON)
set(OLAF_DIR "${CMAKE_CURRENT_SOURCE_DIR}/olaf")
file(STRINGS "${CMAKE_CURRENT_SOURCE_DIR}/olaf.version" OLAF_REVISION LIMIT_COUNT 1)
if(NOT EXISTS "${OLAF_DIR}/src/olaf_fft.c")
    message(FATAL_ERROR "Olaf ausente. Execute native/build.ps1 primeiro.")
endif()
execute_process(COMMAND git -C "${OLAF_DIR}" rev-parse HEAD
    OUTPUT_VARIABLE ACTUAL_REVISION OUTPUT_STRIP_TRAILING_WHITESPACE COMMAND_ERROR_IS_FATAL ANY)
if(NOT ACTUAL_REVISION STREQUAL OLAF_REVISION)
    message(FATAL_ERROR "Commit Olaf diferente de olaf.version. Execute native/build.ps1.")
endif()

# Somente extração: não compila os bancos LMDB/memória nem a CLI do upstream.
add_library(olaf SHARED olaf_wrapper.c
    "${OLAF_DIR}/src/olaf_config.c"
    "${OLAF_DIR}/src/olaf_fft.c"
    "${OLAF_DIR}/src/olaf_ep_extractor.c"
    "${OLAF_DIR}/src/olaf_fp_extractor.c"
    "${OLAF_DIR}/src/olaf_max_filter_perceptual_van_herk.c"
    "${OLAF_DIR}/src/pffft.c")
target_include_directories(olaf PRIVATE "${OLAF_DIR}/src")
target_compile_definitions(olaf PRIVATE PFFFT_SIMD_DISABLE SHAZAMZIN_OLAF_BUILD
    OLAF_REVISION="${OLAF_REVISION}")
if(MSVC)
    target_compile_definitions(olaf PRIVATE _USE_MATH_DEFINES _CRT_SECURE_NO_WARNINGS)
else()
    target_compile_options(olaf PRIVATE -ffp-contract=off)
    target_link_libraries(olaf PRIVATE m)
    if(MINGW)
        target_link_options(olaf PRIVATE -static-libgcc)
    endif()
endif()
set_target_properties(olaf PROPERTIES PREFIX "lib" OUTPUT_NAME olaf)
add_executable(shazamzin-olaf olaf_cli.c)
target_link_libraries(shazamzin-olaf PRIVATE olaf)
foreach(config DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
    set_target_properties(olaf shazamzin-olaf PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY_${config} "${CMAKE_BINARY_DIR}")
endforeach()
