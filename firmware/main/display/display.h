#pragma once
// Display OLED SSD1306 128x64 via I2C (esp_lcd). Sinal de vida da Fase 4.
// Pinos: SDA=GPIO21, SCL=GPIO22 (ver docs/fritzing/README.md). Endereço I2C 0x3C.
//
// Mantém um framebuffer 128x64 (1 bpp) em RAM; as funções de desenho escrevem
// nele e display_flush() empurra tudo pro painel de uma vez.
#include <stdint.h>
#include "esp_err.h"

// Inicializa o barramento I2C, o panel_io e o painel SSD1306. Retorna ESP_OK
// ou o erro do esp_lcd. Em caso de falha, as demais funções viram no-op.
esp_err_t display_init(void);

// Zera o framebuffer (não envia nada ao painel; chame display_flush depois).
void display_clear(void);

// Envia o framebuffer inteiro para o OLED.
void display_flush(void);

// Desenha 's' (ASCII imprimível) na coluna de pixel 'x' e na "página" de texto
// 'page' (cada página = 8 pixels de altura; 0..7 para um painel de 64 linhas).
// Fonte 6x8: cada caractere ocupa 6 px de largura.
void display_text(int x, int page, const char *s);

// Barra de nível horizontal preenchida de 0 a 100%, ocupando a largura toda
// na página 'page'. Usada como VU meter do microfone.
void display_vu(int page, int level_0_100);
