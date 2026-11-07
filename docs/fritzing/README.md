# Planta Fritzing — Shazam-ESP32

Sketch pronto (`Shazamzin.fzz`) com **ESP32 DevKit V1 (30 pinos) + INMP441 (microfone I²S)
+ OLED SSD1306 (I²C) + protoboard full+**, já com todas as 12 ligações elétricas feitas.
Peças reais da comunidade Fritzing, não desenhadas na mão — ver proveniência abaixo.

## Como abrir

As 3 peças usadas (ESP32, INMP441, OLED) **não vêm instaladas por padrão no Fritzing** —
são contribuições da comunidade. É um passo único:

1. Abra o Fritzing.
2. Arraste os 3 arquivos de `parts/` para a aba **MINE** da caixa de peças (parts bin),
   um de cada vez (ou use **Parts → Import...**):
   - `parts/ESP32 DevKit v1 30 pinos.fzpz`
   - `parts/INMP441.fzpz`
   - `parts/SSD1306 OLED 0.96in I2C.fzpz`
3. Abra `Shazamzin.fzz` (File → Open).
4. Vá na aba **Breadboard View**. As 3 peças + a protoboard devem aparecer já posicionadas
   e ligadas.

Se alguma ligação aparecer meio torta visualmente, é só arrastar a peça ou o fio —
a **conexão elétrica já está correta** (é a parte que importa e que dá trabalho fazer
na mão); o ajuste visual é cosmético.

## O que está ligado

| Sinal | ESP32 | → | Destino |
|---|---|---|---|
| 3.3V | pino `3V3` | → | trilho **+** da protoboard |
| GND | pino `GND` | → | trilho **–** da protoboard |
| SDA (I²C) | `GPIO21` | → | OLED `SDA` (direto) |
| SCL (I²C) | `GPIO22` | → | OLED `SCL` (direto) |
| WS (I²S) | `GPIO15` | → | INMP441 `WS` (direto) |
| SCK (I²S) | `GPIO14` | → | INMP441 `SCK` (direto) |
| SD (I²S) | `GPIO32` | → | INMP441 `SD` (direto) |

A protoboard só carrega os trilhos de alimentação: OLED (`VCC`/`GND`) e INMP441
(`VDD`/`GND`/`L-R`) puxam 3.3V e GND dos mesmos trilhos que o ESP32 alimenta — exatamente
como na montagem física (ver `docs/hardware-diagram.html` / arquitetura §4.3).
`L/R` do INMP441 vai no trilho **–** (GND), selecionando o canal esquerdo.

Sinais de dados (I²C, I²S) vão **direto** do ESP32 pro módulo — não passam pela
protoboard, porque não há fan-out nessas linhas (cada uma liga só dois pontos).

## Proveniência das peças (créditos)

Nenhuma peça foi desenhada do zero — todas são contribuições reais baixadas da
comunidade Fritzing, com moduleId/conectores conferidos no `.fzp` de cada uma antes de
montar o sketch:

- **ESP32 DevKit V1 (30 pinos)** — Werner Rothschopf, modificado por vanepp (dez/2018).
  [Fórum Fritzing](https://forum.fritzing.org/t/doit-esp32-devkit-v1/6158/8)
- **INMP441** — Graham Coulby (out/2020).
  [github.com/gcoulby/FritzingParts](https://github.com/gcoulby/FritzingParts)
- **SSD1306 0.96" 128x64 I²C** — Old_Grey / nekocodeX (out/2020), CC BY-NC-SA 3.0 (uso
  não comercial — ok para projeto acadêmico).
  [github.com/matiasfigue/Fritzing-Parts-SSD1306...](https://github.com/matiasfigue/Fritzing-Parts-SSD1306-0.96in-128x64-I2C-Monochrome-OLED-Display)
- **Protoboard** — `BreadboardModuleID` (core do próprio Fritzing, já vem instalada).

## Limitação conhecida

As posições/curvas dos fios no sketch foram calculadas a partir das coordenadas reais
de cada `.fzp`/`.svg`, não desenhadas a olho — mas eu não tenho o Fritzing instalado
aqui pra abrir e conferir visualmente antes de te entregar. A **conectividade elétrica
está verificada e correta** (moduleId, connectorId e grafo de ligações conferidos um a
um); o que não pude confirmar é se os fios caem *exatamente* em cima dos pinos no
desenho ou ficam um pouco deslocados (nesse caso, é só arrastar — não precisa refazer
nenhuma ligação). Se algo vier muito errado ao abrir, me avise com um print que eu ajusto.
