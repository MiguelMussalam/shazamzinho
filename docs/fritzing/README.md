# Planta Fritzing — Shazam-ESP32

`Shazamzin.fzz` é um rascunho editável com **ESP32 DevKit V1 (30 pinos), INMP441,
OLED SSD1306 e protoboard**.

**Mantenha a montagem desligada enquanto corrige as ligações abaixo.** A revisão
da vista de protoboard encontrou conexões involuntárias no arquivo salvo.

- As duas fileiras do INMP441 ocupam as mesmas colunas da metade A–E. A coluna 20
  une `SCK` a `SD`, a 21 une `WS` a `VDD` e a 22 une `L/R` a `GND`. Coloque o
  módulo fora da protoboard e ligue cada pino por jumper, ou posicione os pinos
  em ilhas eletricamente separadas.
- O fio de `L/R` termina no trilho superior ligado ao `VCC` do OLED. Remova
  esse fio e ligue `L/R` diretamente a `GND` para selecionar o canal esquerdo.
  Faça isso antes de alimentar os trilhos.
- As pontes de alimentação estão na extremidade direita. As interrupções centrais
  no trilho superior de GND e no trilho inferior de 3,3 V deixam o OLED sem
  alimentação correta. Alimente `VCC` e `GND` do OLED diretamente pelos pinos do
  ESP32 ou pelos mesmos segmentos já alimentados. Com o circuito desligado,
  confira continuidade das ligações e separação entre 3,3 V e GND.

[Datasheet do INMP441](https://product.tdk.com/system/files/dam/doc/product/sw_piezo/mic/mems-mic/data_sheet/inmp441.pdf)

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
4. Vá na aba **Breadboard View** para conferir a montagem e aplicar os ajustes acima.

## Pinagem prevista

| Sinal | ESP32 | → | Destino |
|---|---|---|---|
| 3.3V | pino `3V3` | → | trilho **+** da protoboard |
| GND | pino `GND` | → | trilho **–** da protoboard |
| SDA (I²C) | `GPIO21` | → | OLED `SDA` (direto) |
| SCL (I²C) | `GPIO22` | → | OLED `SCL` (direto) |
| WS (I²S) | `GPIO15` | → | INMP441 `WS` (direto) |
| SCK (I²S) | `GPIO14` | → | INMP441 `SCK` (direto) |
| SD (I²S) | `GPIO32` | → | INMP441 `SD` (direto) |

Ligue `VCC` do OLED e `VDD` do microfone a 3,3 V. Ligue `GND` dos dois módulos
e `L/R` do microfone ao GND do ESP32. Use os segmentos da protoboard com
continuidade confirmada para distribuir a alimentação.

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
