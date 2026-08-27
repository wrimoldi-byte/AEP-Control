# AEP Control v2.24

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

## Cambios de v2.24

- **Configuración** ahora se abre siempre delante de la aplicación y bloquea correctamente la ventana principal hasta guardar o cancelar.
- Se pueden agregar, quitar y guardar más códigos EDITS desde el cuadro de configuración.
- Nueva cabecera profesional con agua y un avión LATAM, integrada dentro del ejecutable.
- Nueva paleta operativa azul, botones uniformes, filas alternadas y selección destacada sin reducir la legibilidad.

## Cambios de v2.23

- Se reforzó la lectura de **Origen** en llegadas y **Destino** en salidas.
- Cada captura se procesa dos veces y los resultados se fusionan por número de vuelo.
- Los códigos IATA se validan y se corrigen confusiones frecuentes como `G1G/GIG`, `L1M/LIM` y `P0A/POA`.
- Durante la lectura continua se elige el aeropuerto más reconocido, evitando que un error aislado quede guardado.

## Cambios de v2.22

- El botón **Leer datos de salida** ahora se llama **INFO DE ITO**.
- La pantalla ITO se procesa completa y también por sectores separados para mejorar la lectura de datos pequeños y de distintos colores.
- Se comparan múltiples resultados OCR antes de elegir matrícula, configuración y servicios.
- Se reforzó la corrección de confusiones habituales del OCR como `I/1`, `O/0` y `B/8`.
- Si el número de vuelo no se reconoce correctamente, se utiliza la salida seleccionada en la grilla en lugar de perder la captura.

## Funciones incorporadas en v2.21

- Nuevo botón **Leer datos de salida**: captura el cuadro operativo y lo relaciona con el vuelo por número.
- El OCR extrae **matrícula**, **configuración de aeronave** y los servicios `HLDL`, `HLDR`, `SPMLJ` y `SPMLY`.
- Los datos se exportan en las columnas **MATRÍCULA**, **CONF** y **SVCS** de la misma fila de salida.
- Los servicios con valor cero no recargan la planilla; si todos están en cero se informa `SIN SERVICIOS`.

## Mejoras conservadas de v2.20

- El Excel conserva PAX como `PE/Economy` (por ejemplo `7/14`) y ya no suma ambas cabinas.
- La lectura continua de EDITS reconoce `INF` y `ETO` y los exporta en sus columnas dedicadas.
- El Excel usa un formato operativo profesional con bloques separados de arribos y salidas, encabezados jerarquizados, filas alternadas, ETD destacado y panel congelado.
