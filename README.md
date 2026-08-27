# AEP Control v2.20

Prototipo portátil para Windows que lee por OCR la tabla de vuelos del siguiente turno.

## Primera función

1. Abrir `AEPControl.exe`.
2. Presionar **Capturar tabla de vuelos**.
3. Marcar con el mouse solamente la tabla de Sabre.
4. Revisar los datos detectados: vuelo, destino, hora, equipo, Premium, Economy y total.

El OCR se procesa localmente con el motor de Windows. No se conecta a Sabre ni envía información a internet.

## Descargar el EXE

Entrar en **Actions**, abrir la ejecución más reciente y descargar el artefacto **AEPControl-Windows-v0.1**.

## Requisitos

- Windows 10 u 11 de 64 bits.
- Algún idioma de reconocimiento óptico instalado en Windows.

Esta versión es una prueba inicial. El resultado debe revisarse antes de utilizarlo operativamente.

## Cambios de v2.20

- El Excel conserva PAX como `PE/Economy` (por ejemplo `7/14`) y ya no suma ambas cabinas.
- La lectura continua de EDITS reconoce `INF` y `ETO` y los exporta en sus columnas dedicadas.
- El Excel usa un formato operativo profesional con bloques separados de arribos y salidas, encabezados jerarquizados, filas alternadas, ETD destacado y panel congelado.
