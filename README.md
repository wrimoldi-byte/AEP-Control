# AEP Control v2.26.0

Prototipo portátil para Windows que lee por OCR la tabla de vuelos del siguiente turno.

## Primera función

1. Abrir `AEPControl.exe`.
2. Presionar **Capturar tabla de vuelos**.
3. Marcar con el mouse solamente la tabla de Sabre.
4. Revisar los datos detectados: vuelo, destino, hora, equipo, Premium, Economy y total.

El OCR se procesa localmente con el motor de Windows. No se conecta a Sabre ni envía información a internet.

## Descargar el EXE

Entrar en **Actions**, abrir la ejecución más reciente y descargar el artefacto **AEP-Control-v2.26.0-win-x64**.

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

## v2.25: captura IA y edición manual

- Selector **OCR local (Windows) / IA Gemini (captura)**. El modo local sigue funcionando sin conexión.
- En IA, **Leer llegadas**, **Leer salidas**, **INFO DE ITO** y **Leer EDITS** capturan directamente una imagen y la envían a Gemini. No pasa por OCR de Windows. Documentación PAX conserva exclusivamente su lectura local.
- Una consulta por clic. Para listas largas, capturar cada página quieta. Los vuelos se fusionan y EDITS se acumula por identidad visible del pasajero; repetir una misma fila identificada no suma dos veces. Identidades mal leídas aún requieren revisión.
- Revisar el recorte antes de enviarlo y la respuesta antes de cargarla. Los campos ilegibles quedan vacíos y se presentan advertencias.
- **Configurar IA**: ingresar clave propia de Google AI Studio y modelo con visión. La clave se cifra con DPAPI del usuario Windows y no se incluye en código ni en el EXE.
- Usar un proyecto de Google **sin facturación habilitada**. La aplicación no puede verificar ese estado ni garantizar que el proveedor mantenga una cuota gratuita. No habilita facturación, cambia modelos o reintenta consultas automáticamente. HTTP 429 detiene la consulta. Límite local configurable, 100 solicitudes diarias por defecto (también cuentan intentos fallidos).
- Google puede utilizar contenido del nivel gratuito para mejorar productos. No enviar datos personales, sensibles o confidenciales. Probar con capturas ficticias o anonimizadas; verificar la política de la empresa antes de usar imágenes operativas. No se envía nada al ejecutar pruebas automáticas.
- **Doble clic o F2** en una celda permite corregirla directamente en la tabla, sin otra ventana. **Enter**, **Tab** o cambiar de celda guarda; **Esc** cancela. Se pueden corregir vuelo, aeropuerto, hora, equipo, PE/ECO, datos ITO de salidas y EDITS. Separar códigos con punto y coma: `WCHR 2; INF 1; ETO 3`. Borrar un código lo quita; borrar todo elimina las cantidades. Para confirmar cero en EDITS aún no leídos, escribir `WCHR 0`. Los errores se indican en la celda y en el estado, sin ventanas emergentes.
- Campos modificados se marcan **Manual**, se conservan frente a nuevas capturas locales o IA y se exportan al Excel, incluyendo INF/ETO en columnas propias. **Permitir releer** desprotege los campos y reinicia la acumulación IA de EDITS, sin borrar los valores actuales hasta una lectura nueva.
- La regla WCHC > WCHS > WCHR solo se aplica al mismo pasajero en capturas IA. Pasajeros distintos se cuentan por separado.
- Los datos del turno y sus protecciones permanecen en memoria durante la sesión; Reiniciar o cerrar limpia el turno. Exportar Excel antes de cerrar.

### Verificación

`dotnet run --project Tests/Tests.csproj --configuration Release` (Windows y .NET 8).
Pruebas de respuestas estructuradas, 3/5 sin reemplazos arbitrarios, deduplicación, prioridad de sillas por pasajero, edición real WinForms, protección de correcciones, DPAPI, solicitudes simuladas, errores de cuota y exportación XLSX. La lectura real necesita una clave propia y capturas de prueba; no se garantiza exactitud del modelo.

## v2.25.1

Configuración IA, editor y revisión de capturas se abren delante de la ventana principal. Configurar IA muestra instrucciones de clave, modelo y límite. El enlace a Google AI Studio minimiza el diálogo para dejar trabajar en el navegador; volver a la configuración desde la barra de tareas después de copiar la clave.

## v2.25.2

Refuerza las instrucciones de lectura de la hora por fila y movimiento. Normaliza horas H:mm, HHmm, HH:mm:ss, AM/PM y fechas seguidas de hora a HH:mm. Cada hora ilegible, ausente o inválida genera una advertencia por vuelo antes de cargar. No inventa horarios ni convierte zonas horarias.

## v2.26.0

Edición directa en las tablas de llegadas y salidas. Los cambios confirmados actualizan el mismo vuelo y el Excel; no se abre el editor aparte. Las correcciones conservan su protección ante OCR/IA. Revisión es una columna informativa. Las columnas tienen un ancho mínimo legible y desplazamiento horizontal.
