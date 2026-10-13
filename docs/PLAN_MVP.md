# Fuser Song Manager v2 — Plan de implementación del MVP

> Complementa `FUSER_SONG_MANAGER_PROYECTO.md` (arquitectura) y `FUSER_REQUISITOS_FUNCIONALES.md` (requisitos). Este documento define **qué entra en el MVP, en qué orden se construye y cuándo se da por terminado**.

| Campo | Valor |
|---|---|
| Estado | MVP completado (fases 0–4) |
| Fecha | 12 de octubre de 2026 |
| Punto de partida | Solución `FuserSongManager.slnx` creada (net10.0, Avalonia 12.1.4, CUE4Parse, Microsoft.Data.Sqlite, SharpCompress, CommunityToolkit.Mvvm) con proyectos vacíos |

---

## 1. Definición del MVP

**Objetivo:** que un usuario de la v1 pueda reemplazarla por la v2 en su uso diario sin perder datos, y que note la mejora en rapidez y fluidez.

El MVP es la **v2.0 recortada**: incluye todos los requisitos que hacen falta para sustituir a la v1 y deja para después lo que la v1 tampoco tenía o lo que no bloquea el uso diario.

### 1.1 Dentro del MVP

| Área | Requisitos |
|---|---|
| Biblioteca | RF-BIB-01, 02, 03, 04, 05 (paralelismo fijo, sin UI de ajuste), 07, 09, 10 |
| Metadatos | RF-MET-01, 02, 03, 04, 05 |
| Estado | RF-EST-01, 02, 03, 04, 05, 06, 07, 08 |
| Importación | RF-IMP-01, 02, 03, 04, 05, 07, 09 (selección por diálogo de archivo) |
| Usuario | RF-USR-01, 02, 03, 04, 05 |
| Búsqueda | RF-BUS-01, 04, 05, 06, 07; RF-BUS-02 **reducido** (género, estado y rating) |
| Detalle | RF-DET-01, 02, 05 |
| Configuración | RF-CFG-01, 02, 03, 04, 05 |
| Interfaz | RF-UI-01, 02, 04, 05, 06 |
| Sistema | RF-SIS-01 (log en archivo, sin visor), 03, 05 |

### 1.2 Fuera del MVP (siguiente iteración)

- Filtros completos: tonalidad, modo, rango de BPM y de años, autor (resto de RF-BUS-02), RF-BUS-03 y RF-BUS-08.
- Propiedades adicionales y contenido del pak en la UI: RF-MET-06, RF-MET-07, RF-DET-03, RF-DET-04 y RF-BUS-09. La Fase 0 sí las **vuelca** para investigarlas.
- Arrastrar y soltar (RF-IMP-06), resumen detallado (RF-IMP-08) e importación múltiple (RF-IMP-10).
- Columnas configurables, tema elegible y recordar el layout (RF-UI-03, RF-CFG-07, RF-CFG-08). El MVP sigue el tema del sistema.
- Cancelar el escaneo (RF-BIB-06), conservar los datos de canciones borradas (RF-BIB-08) y edición en lote (RF-USR-06).
- Comprobar actualizaciones, exportar e importar la biblioteca, catálogo DLC editable (RF-SIS-02, RF-SIS-04, RF-MET-08).
- Todo lo de la sección PEN (crates, etiquetas, audio, etc.).

### 1.3 Requisitos "Por validar" que se cierran con el código de la v1

Se revisó `manager.py` y `loader.py`. Estos puntos dejan de estar pendientes:

| Requisito | Comportamiento real de la v1 | Decisión para el MVP |
|---|---|---|
| RF-EST-07 | Borra `customSongsUnlocked_P.pak` y `.sig` de `Content/Paks` al refrescar la lista y al lanzar el juego (`manager.py:571`, `manager.py:600`) | Borrarlos tras cualquier cambio de estado, importación o lanzamiento |
| RF-EST-08 | Usa `tasklist` para buscar `Fuser-Win64-Shipping.exe` o `FuserEOS-Win64-Shipping.exe` y, si el juego está abierto, desactiva el cambio de estado (`manager.py:113`, `628`, `645`) | Bloquear habilitar, deshabilitar e importar mientras el juego esté abierto (usando `Process.GetProcessesByName`) |
| RF-CFG-04/05 | Steam: `steam://run/1331440`. Epic: no soportado, muestra un aviso. Local: ejecuta el `.exe`/`.bat` configurado desde su carpeta | Replicarlo igual, incluido el aviso de Epic |
| Rating (decisión abierta 2) | Entero de 0 a 5, mostrado como estrellas | Escala 0–5 |

### 1.4 Hechos de la v1 que afectan la migración

- La base de la v1 está en `%LocalAppData%\FuserCustomSongManager\db\songs.sqlite` y la configuración en `%LocalAppData%\FuserCustomSongManager\config.ini` (claves `fuser_path`, `database_path`, `install_type`, `executable_path`).
- En la v1 la columna del título se llama **`name`**, no `song_name` como dice la sección 6.4 del documento de arquitectura. Hay que corregir el documento o mapearla en la migración.
- Los enums se guardan como enteros: `Genres` del 0 al 9, `SongKey` del 0 al 11 con `Num = 12` como "sin dato", `SongMode` con Major 0, Minor 1 y `Num = 2`, y `LauncherType` con Steam 0, Epic 1 y Local 2. La v2 debe usar **los mismos valores** para que la migración sea directa.
- La v2 usará su propia carpeta (`%LocalAppData%\FuserSongManager\`) y **copiará** la base de la v1, sin modificar la original. Así la v1 sigue funcionando si el usuario quiere volver.

---

## 2. Decisiones para el MVP

Son las respuestas por defecto a las decisiones abiertas. Se pueden cambiar sin alterar la estructura del plan.

| Decisión abierta | Por defecto en el MVP |
|---|---|
| Crates | Fuera del MVP. Nivel A en la siguiente iteración |
| Rating | 0–5 estrellas, igual que la v1 |
| Autor | Texto libre, igual que la v1 |
| Canciones borradas del disco | Se elimina la fila, igual que la v1. RF-BIB-08 queda para después |
| Lanzar el juego | Entra en el MVP, porque la v1 ya lo hace |

---

## 3. Fases y tareas

Cada fase termina en algo que se puede ejecutar y probar. Las fases 1 y 2 no dependen de la UI.

### Fase 0 — Spike técnico con CUE4Parse (bloqueante)

> **Estado (12/10/2026): completada.** Hay paridad 20/20 con la v1 usando CUE4Parse más un lector tolerante. Detalles en `SPIKE_CUE4PARSE.md`.

**Meta:** confirmar que CUE4Parse lee los paks de Fuser y obtener los siete campos por nombre de propiedad. Cierra los riesgos 1 a 4 y avanza el 5.

1. **Paks de muestra.** Reunir de 5 a 10 paks custom variados (de distintos autores y épocas, alguno con subcarpeta) y 1 o 2 DLC parcheados. Guardarlos fuera del repositorio en una ruta configurable por variable de entorno (`FSM_SAMPLE_PAKS`).
2. **`FuserSongManager.Tools` como CLI** con estos comandos:
   - `list <pak>`: lista los archivos que contiene el pak.
   - `dump <pak>`: monta el pak y vuelca a JSON todas las properties de `Meta_*` y `Meta_*bs`.
   - `read <pak>`: muestra los siete campos ya mapeados.
3. **Montar un pak suelto** con `DefaultFileProvider` o `StreamedFileProvider`. Probar los `EGame` cercanos (Fuser usa UE4.2x; empezar por `GAME_UE4_LATEST` y bajar) y confirmar que no hace falta clave AES.
4. **Comparar con la v1.** Script Python `tools/v1_reference_dump.py` que reutiliza `loader.get_song_info` y escribe JSON con los siete campos de cada pak. Un `compare` en Tools compara las dos salidas.
5. **Resultado escrito** en `docs/SPIKE_CUE4PARSE.md`: el `EGame` que funciona, los nombres de las properties de cada campo, cómo vienen las properties de Harmonix (genéricas o no) y el inventario de datos adicionales encontrados.

**Criterio de salida:** los siete campos coinciden con la v1 en todos los paks de muestra.
**Si falla:** plan B del documento de arquitectura, es decir, portar `get_metadata` a C# manteniendo la interfaz `ISongMetadataReader`. El resto del plan no cambia.

### Fase 1 — Núcleo: modelos, lectura y persistencia

> **Estado (12/10/2026): completada.** Se cumple el criterio de salida: `Tools sync` indexa la biblioteca, y una segunda pasada sin cambios no abre ningún pak. Hay 39 pruebas; las 2 de paridad necesitan `FSM_SAMPLE_PAKS`.
>
> **Desviaciones del plan:**
> - Hay una sola migración con el esquema completo, porque la v2 empieza con una base propia.
> - La importación de la v1 la hace `V1Import`, que lee en modo solo lectura, en lugar de una migración.
> - Se añadieron las columnas `relative_path`, `has_signature` y `read_error`.
> - Si el pak fue renombrado, el nombre de la canción se toma del asset que contiene. El juego lo acepta, pero la v1 fallaba con `KeyError` y detenía el arranque.
>
> **Medición** (8 núcleos, caché del sistema caliente, 1000 paks como enlaces duros a 20 reales):
>
> | Caso | v1 | v2 |
> |---|---|---|
> | Primer escaneo completo | 3,5 s | 0,36 s (paralelismo 4) |
> | Arranque sin cambios | ~0 s | 0,05 s |
> | Escaneo forzado, por paralelismo | — | 1 hilo 0,83 s · 2 hilos 0,50 s · 4 hilos 0,35 s · 8 hilos 0,39 s |
>
> El parser de la v1, por sí solo, **no es lento**: unos 0,5 ms por pak, frente a unos 0,8 ms de la v2 en un solo hilo. La diferencia de la v2 viene de enumerar cada carpeta una sola vez (la v1 hacía un `glob` por archivo), del paralelismo y de la detección de cambios por archivo. Por encima de 4 hilos no hay mejora. Falta medir con la caché fría y en un HDD, donde mandará el disco.

1. **Core/Models:** `Song`, `SongMetadata`, `Genre`, `MusicalKey`, `SongMode`, `LauncherType` (con los valores numéricos de la v1), `ReadStatus` (Ok, Partial, Failed más el motivo), `AppSettings` y `SongLocation` (habilitada o deshabilitada, más la ruta relativa).
2. **Core/Abstractions:** `ISongMetadataReader`, `ISongRepository`, `ISongFileService`, `IArchiveImporter`, `IGameProcessService` y `ISettingsStore`.
3. **Infra/Pak:** `CueSongMetadataReader`, que traslada a código lo aprendido en la Fase 0, y `PatchedDlcCatalog`, que carga `patched_dlc_metadata.json` como recurso incrustado. Aplicar los valores por defecto de género de RF-MET-05.
4. **Infra/Data:**
   - `SqliteSongRepository` con consultas parametrizadas y escritura por lotes dentro de una transacción.
   - Migraciones versionadas con una tabla `schema_version`. La migración 1 crea el esquema v2 y la 2 añade las columnas nuevas.
   - `V1DatabaseImporter`: si existe la base de la v1 y la de la v2 no, copia las filas y mapea `name` a `song_name`. Además se lee el `config.ini` de la v1 para precargar la configuración.
5. **Core/Services:**
   - `LibraryScanner`: una sola enumeración recursiva de cada carpeta que devuelve el índice `filename → (ruta, tamaño, mtime, ubicación)`.
   - `LibrarySyncService`: calcula el plan de cambios (nuevas, cambiadas, movidas, eliminadas) y parsea en paralelo con `Parallel.ForEachAsync` (paralelismo de `Environment.ProcessorCount / 2` como mínimo 2). Reporta el avance con `IProgress<ScanProgress>`.
6. **Configuración:** `JsonSettingsStore` en `%LocalAppData%\FuserSongManager\settings.json`.
7. **Pruebas (xUnit):**
   - Sync con repositorio y lector falsos: sin cambios no se lee ningún pak, la canción movida conserva su rating, la canción nueva se lee y la borrada se elimina.
   - Migración de una base v1 de ejemplo creada en la prueba.
   - Paridad contra los paks de muestra. Se marca `[Trait("Category","Samples")]` y se omite si no hay paks.
   - Añadir el proyecto `FuserSongManager.Infrastructure.Tests` a la solución.

**Criterio de salida:** `Tools sync <carpeta Fuser>` indexa una biblioteca real, y una segunda ejecución sin cambios no abre ningún pak.

### Fase 2 — Operaciones de archivos

> **Estado (12/10/2026): completada.** Los comandos `enable`, `disable` e `import` de Tools se probaron sobre una instalación de prueba con zip, rar (normal y sólido), 7z y paks sueltos. Hay 71 pruebas en total.
>
> **Comportamiento decidido** (y diferencias con la v1):
> - **Habilitar sin `.sig`:** se rechaza y se informa del archivo que falta. La v1 deshabilitaba la canción en silencio. Deshabilitar sin `.sig` sí se permite: se mueve solo el `.pak`.
> - **Destino ocupado:** no se sobrescribe nada y se pide resolverlo a mano (RF-EST-06).
> - **Fallo a mitad de un movimiento:** se deshace el primer movimiento. Si incluso eso falla, el mensaje dice qué archivo hay que devolver a su sitio.
> - **Carpetas vacías:** las subcarpetas que quedan vacías en el origen se borran, pero nunca la raíz de canciones.
> - **Importar una canción ya instalada:** se reemplaza en su ubicación actual, como en la v1 (RF-IMP-05). Con la opción `ReplaceExisting = false` se omite. La v1 omitía la canción si estaba en la carpeta contraria; la v2 la actualiza.
> - **Reemplazo seguro:** los archivos nuevos se copian primero junto al destino y luego se intercambian. Si algo falla, se restauran los anteriores, así que nunca quedan un `.pak` y un `.sig` de versiones distintas.
> - **Contenido del comprimido:** las parejas se emparejan por nombre en cualquier carpeta, como en la v1. Las copias repetidas de una misma canción se omiten como ambiguas. Se ignora la basura de macOS (`__MACOSX`, `._*`). Los destinos se construyen solo con el nombre de archivo, nunca con rutas del comprimido, así que no hay riesgo de *zip slip*.
> - **Juego abierto:** se bloquean habilitar, deshabilitar e importar (RF-EST-08) y no se puede lanzar dos veces. Lanzar con Epic sigue sin soporte, igual que en la v1.
> - **Caché del juego:** `customSongsUnlocked_P.*` se borra después de cada cambio de estado o importación y antes de lanzar (RF-EST-07).

1. **`SongFileService`** para habilitar y deshabilitar:
   - Mueve el `.pak` y el `.sig` como una unidad y conserva la subcarpeta relativa.
   - Antes de mover comprueba que exista el `.sig` y que no haya conflicto en el destino.
   - Si el segundo movimiento falla, deshace el primero.
   - Después borra `customSongsUnlocked_P.*`.
2. **`GameProcessService`:** detecta si el juego está abierto y lo lanza según el tipo de lanzador.
3. **`SharpCompressImporter`:**
   - Extrae a `%Temp%\FuserSongManager\<guid>` y busca las parejas `.pak`/`.sig` en cualquier profundidad.
   - Respeta la ubicación actual si la canción ya existe (RF-IMP-05).
   - Limpia la carpeta temporal en un `finally` y devuelve un `ImportResult` con las canciones importadas, omitidas y fallidas.
4. **Pruebas** sobre carpetas temporales:
   - Mover y revertir.
   - Falta del `.sig`.
   - Conflicto en el destino.
   - Zip con canciones anidadas y un pak sin su `.sig`.

**Criterio de salida:** los comandos `enable`, `disable` e `import` de Tools funcionan sobre una copia de la carpeta de Fuser.

### Fase 3 — Interfaz (Avalonia + MVVM)

> **Estado (12/10/2026): completada.** La interfaz está en inglés. Hay 121 pruebas en total; 26 son de la UI y se ejecutan sin ventana visible con Avalonia.Headless y Skia. Esas pruebas recorren los flujos completos sobre una instalación temporal con SQLite, movimientos de archivos e importador reales; solo el lector de paks, el proceso del juego y los diálogos son falsos. Con `FSM_SCREENSHOTS` guardan capturas de cada ventana, y con `FSM_SAMPLE_PAKS` muestran los 20 paks reales leídos con CUE4Parse. La app real arrancó e indexó 1000 paks sin errores.
>
> **Desviaciones y decisiones:**
> - **Sin contenedor de dependencias.** `AppServices` hace de raíz de composición escrita a mano y recibe la infraestructura por interfaces, así que las pruebas pueden cambiarla. Para el tamaño actual, un contenedor no aporta nada.
> - **`DataGrid`:** se usa `Avalonia.Controls.DataGrid` 12.1.2 sobre un `DataGridCollectionView` que filtra y ordena en memoria. Con 2000 canciones, buscar tarda unos 6 ms y ordenar unos 8 ms (criterio 8).
> - **Protección ante una carpeta que no existe:** si la carpeta de Fuser no se encuentra (por ejemplo, un disco desconectado), no se escanea. Así la biblioteca no se vacía, porque el escaneo habría dado todas las canciones por borradas. Se muestra el aviso y las acciones de archivos quedan desactivadas.
> - **Detección del juego:** se consulta cada 2 segundos en segundo plano. Mientras está abierto, habilitar, deshabilitar, importar y lanzar quedan desactivados, con una explicación junto a los botones.
> - **Primer arranque:** la carpeta se rellena desde la configuración de la v1 o desde la ruta habitual de Steam. Se acepta también una carpeta interior, como `Paks` o `custom_songs`, y se sube hasta la raíz de la instalación. La importación de la v1 viene marcada si hay biblioteca.
> - **Detalle:** el rating se guarda al hacer clic; volver a pulsar la misma estrella lo borra. Notas y autor se guardan al perder el foco. Un reescaneo no pisa el texto que se está escribiendo.
> - **Extras que no estaban previstos:**
>   - "Read every song again" (RF-BIB-09) e "Import as disabled…" (RF-IMP-04).
>   - Atajo Ctrl+F para buscar.
>   - Filtro "With problems".
>   - Filas deshabilitadas atenuadas.
>   - Variable `FSM_DATA_DIR` para probar la app con otra carpeta de datos.
> - **Corregido en las pruebas:** los avisos de progreso llegaban tarde y sobrescribían el resumen del escaneo en la barra de estado.

1. **Arranque:**
   - Contenedor de dependencias en `App.axaml.cs`.
   - Asistente de primer arranque: elegir la carpeta, validar que existe `Fuser/Content/Paks`, crear las carpetas que falten y ofrecer la importación de la v1.
2. **Ventana principal:**
   - `DataGrid` virtualizado con título, artista, año, género, key, modo, BPM, estado, rating y autor.
   - Al ordenar título y artista se ignoran "The " y "A " (comparador en Core).
   - Indicador visual para las filas con `read_status` distinto de `ok` o sin `.sig`.
3. **Búsqueda y filtros:**
   - Caja de búsqueda con un debounce de unos 150 ms que filtra en memoria.
   - Filtros de género, estado y rating mínimo.
   - Contador "N de M".
4. **Panel de detalle:**
   - Muestra todos los campos, el estado de lectura y su motivo.
   - Permite editar rating, notas y autor, y guarda al perder el foco.
   - Botón "Abrir carpeta".
5. **Barra de acciones:**
   - Habilitar o deshabilitar la selección múltiple, pidiendo confirmación si hay más de una.
   - Importar, reescanear, lanzar Fuser y abrir la configuración.
   - Si el juego está abierto, las acciones que tocan archivos se desactivan con una explicación.
6. **Estado del escaneo:** barra de progreso "120 de 480" en la parte inferior. La lista guardada aparece antes de que empiece el escaneo.
7. **Ventana de configuración:** carpeta de Fuser, lanzador y ruta del ejecutable.

**Criterio de salida:** los flujos de la sección 4 se completan de principio a fin en la UI.

### Fase 4 — Estabilización y entrega

> **Estado (12/10/2026): completada.** Hay 125 pruebas. `tools/publish.ps1` ejecuta las pruebas, publica y genera `artifacts/FuserSongManager-2.0.0-win-x64.zip` (83 MB): un único `.exe` autocontenido, un README y `THIRD-PARTY-NOTICES.txt`.
>
> **Logging:**
> - Serilog escribe en `%LocalAppData%\FuserSongManager\logs\fsm-AAAAMMDD.log`: un archivo por día, se conservan 14 y cada uno tiene un máximo de 10 MB.
> - Se registran el arranque (versión y carpeta de datos), el tiempo hasta mostrar la lista y el resumen de cada sincronización.
> - Cada pak que falla o se lee parcialmente queda registrado con su nombre, su ruta y el motivo; para eso `SyncResult.ReadProblems` sustituye al contador `Failed`.
> - También se registran los duplicados, los movimientos fallidos, las importaciones (cada canción y cada omisión), el lanzamiento y los cambios de configuración.
> - En Configuración hay un botón "Open log folder".
> - CUE4Parse escribe en el logger estático de Serilog una línea por cada pak montado. La app usa su propio logger y el estático solo deja pasar advertencias y errores.
>
> **Errores globales:**
> - Un error en el hilo de la UI se registra, se muestra con un mensaje claro y la app sigue funcionando.
> - Las excepciones no observadas y las fatales se registran.
> - Si la app no puede arrancar (por ejemplo, la base está bloqueada), muestra una ventana con el motivo y la carpeta de logs, en lugar de cerrarse sin decir nada.
>
> **Corregido durante la medición:** la sincronización, los movimientos y la importación se ejecutaban en el hilo de la UI. Cada `await` esperaba a que la interfaz quedara libre, así que una sincronización sin cambios tardaba unos 290 ms en lugar de unos 40. Además, con un lote grande de movimientos la ventana se habría congelado (RF-UI-02). Ahora se ejecutan con `Task.Run`.
>
> **Dependencias:** la NU1903 se resolvió fijando `Microsoft.Bcl.Memory` 10.0.12 en Infrastructure. Era una dependencia transitiva de CUE4Parse → Fmod5Sharp → IndexRange. La solución compila con 0 advertencias.
>
> **Licencias:** todas son permisivas (MIT, Apache-2.0, BSD-2/3; la fuente Inter usa OFL-1.1). La DLL propietaria de Oodle no se distribuye y los paks de Fuser no la necesitan. El listado completo está en `tools/THIRD-PARTY-NOTICES.txt` y va dentro del ZIP.
>
> **Publicación:** ReadyToRun activado y compresión del single-file desactivada, después de medir las tres opciones en la biblioteca real:
>
> | Variante | Lista visible | Escaneo completo | Tamaño del `.exe` / ZIP |
> |---|---|---|---|
> | Sin ReadyToRun, comprimido | 1,7 s | 0,43 s | 58 MB / — |
> | ReadyToRun, comprimido | 1,0 s | 0,21 s | 89 MB / 79 MB |
> | **ReadyToRun, sin comprimir** | **0,65–0,8 s** | **0,21–0,27 s** | 193 MB / 83 MB |
>
> **Medición final** (ejecutable publicado; biblioteca real de 254 paks y 2,6 GB, con 4 duplicados que se ignoran; caché del sistema caliente; SSD):
>
> | Caso | v1 | v2 (250 canciones) | v2 (1000 canciones) |
> |---|---|---|---|
> | Lista visible desde que arranca el proceso | no medido* | 0,65–0,8 s | 0,65–0,7 s |
> | Escaneo completo | 1,17 s | 0,24–0,27 s | 0,41–0,45 s |
> | Sincronización sin cambios, en segundo plano | 0,03 s** | 0,04–0,05 s | 0,02–0,03 s |
>
> \* Medirlo exigía abrir la v1 contra la configuración real, y la v1 borra la caché del juego al refrescar.
> \*\* La v1 solo compara la fecha de las carpetas: no detecta un pak reemplazado con el mismo nombre ni los archivos sueltos que se cambian a mano. Por eso tu base de la v1 tenía 223 canciones y en disco hay 250.
>
> La caché fría y el HDD siguen sin medir: no hay HDD disponible y vaciar la caché del sistema requiere permisos de administrador.
>
> **Prueba de migración** con una copia de la base real de la v1, de 223 filas; la base original no cambió (mismo hash):
> - Las 223 filas llegan a la v2, con el estado idéntico.
> - Como la base real no tenía datos de usuario, se añadieron ratings, notas y autores sintéticos a la copia; todos se conservan.
> - Los siete campos coinciden con la v1 en las 223 canciones; la paridad pasa así de 20 paks a 223.
> - Aparecen 27 canciones nuevas, que estaban en disco pero la v1 nunca indexó.
>
> Para repetir la prueba hay un script en el scratchpad de la sesión (`migration_check.py`); la parte de importación también está cubierta por `V1ImportTests`.

1. **Logging:** Serilog con un archivo rotativo en `%LocalAppData%\FuserSongManager\logs`, que incluya el nombre del pak en cada error. Además, un manejador global de excepciones que registra el error y muestra un mensaje claro.
2. **Medición de rendimiento** con una biblioteca grande, real o sintética (paks de muestra copiados con N nombres):
   - Tiempo hasta que se muestra la lista.
   - Escaneo completo.
   - Sincronización sin cambios.
   - Comparación con la v1.
   - Los resultados se anotan en `docs/`.
3. **Publicación:** `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` y un ZIP portable.
4. **Prueba de migración** con la base real de un usuario de la v1, usando una copia.

---

## 4. Criterios de aceptación del MVP

> **Estado (12/10/2026):**
>
> | # | Estado | Evidencia |
> |---|---|---|
> | 1 | Cumplido | El asistente toma la carpeta y el lanzador de la v1 e importa su base (`FirstRunViewModel`, migración con la base real) |
> | 2 | Cumplido | 20 paks de muestra más 223 canciones reales, sin diferencias |
> | 3 | Cumplido | La lista aparece en 0,65–0,8 s; un arranque sin cambios lee 0 paks |
> | 4 | Cumplido | Escaneo, importación y movimientos fuera del hilo de la UI; probado solo con pruebas automáticas, no a mano con el ratón |
> | 5 | Cumplido | El pak ilegible queda marcado con ⚠ y su motivo, y el escaneo continúa (pruebas de UI y de sync) |
> | 6 | Cumplido | Movimiento con reversión, `.sig` obligatorio y bloqueo con el juego abierto (pruebas de Fase 2 y de UI) |
> | 7 | Cumplido | Un movimiento manual se detecta sin releer el pak y conserva los datos (`LibrarySyncServiceTests`) |
> | 8 | Cumplido | Con 2000 canciones, buscar tarda unos 6 ms y ordenar unos 8 ms (`PerformanceTests`) |

1. Un usuario de la v1 abre la v2 por primera vez y ve su biblioteca con rating, notas y autor intactos, sin haber configurado nada a mano más que confirmar la carpeta.
2. Los siete campos coinciden con la v1 en todos los paks de muestra.
3. Con la biblioteca indexada, la lista aparece en menos de 1 segundo, y un arranque sin cambios no abre ningún pak.
4. La ventana sigue respondiendo durante el escaneo y la importación.
5. Un pak corrupto aparece marcado en la lista y no detiene el escaneo.
6. Las operaciones de habilitar, deshabilitar e importar nunca dejan un `.pak` sin su `.sig`, y se bloquean con el juego abierto.
7. Mover un pak a mano entre carpetas se refleja como cambio de estado en el siguiente arranque, sin perder datos.
8. Buscar y ordenar se perciben como instantáneos con al menos 2000 canciones.

---

## Después del MVP: portada y reproductor (RF-PEN-14, RF-PEN-13)

> **Estado (12/10/2026): implementado.** Hay 161 pruebas y 0 advertencias. Los detalles técnicos y la investigación están en la sección 4.1 de `SPIKE_CUE4PARSE.md`.

**Portada:**
- El panel de detalle muestra la portada de 512×512 que trae el pak. Si el pak no tiene portada, se muestra un hueco con el motivo.
- Se lee la primera vez que se selecciona la canción (unos 3 ms más lo que tarda abrir el pak) y se guarda en `%LocalAppData%\FuserSongManager\art`.
- La entrada de caché depende del tamaño y la fecha del pak: si se reemplaza el pak, se vuelve a leer y la entrada antigua se borra. También se recuerda qué paks no tienen portada.
- Si un pak está dañado, no aparece portada y se vuelve a intentar la próxima vez.

**Reproductor:**
- Tiene reproducir y pausar, detener, barra de posición y un interruptor por parte (Beat, Bass, Loop y Lead), como al mezclar en el juego.
- El pak solo se lee al pulsar ▶; leerlo y descifrarlo tarda unos 0,8 s y se hace en segundo plano.
- El audio se decodifica mientras suena (NVorbis) y sale por NAudio/WinMM. No se escribe nada en disco y no hay exportación de audio.
- Se detiene al cambiar de canción y al lanzar Fuser.
- Una parte vacía (una canción sin bajo) aparece desactivada y lo explica.
- Si el audio no se puede reproducir (por ejemplo, otra versión de MOGG), se muestra el motivo; los detalles van al log.

**Verificado:**
- Las 254 canciones reales (1802 MOGG) se descifran y decodifican.
- Con los 20 paks de muestra, la portada sale a 512×512, se leen las partes de cada canción y se puede buscar cualquier posición.
- Con el dispositivo de audio real, en silencio para no hacer ruido: la reproducción avanza, una búsqueda al final tarda 17 ms y se detecta el fin de la canción.

**Decisiones:**
- **Sin CUE4Parse-Conversion.** Se descartó porque trae `SixLabors.ImageSharp` con vulnerabilidades conocidas. Se usa un decodificador DXT propio.
- **Capas por rango de notas.** Las zonas se eligen por rango de notas y no por etiqueta.
- **Mezcla aproximada.** Cada parte se multiplica por 0,6 y la suma se recorta a [−1, 1], porque la v2 no replica los efectos ni los volúmenes del juego.

**Corregido al probar el ejecutable publicado:**
- **Selección automática al arrancar.** El `DataGrid` seleccionaba solo la primera fila, porque la vista de la colección empieza con el primer elemento como "actual". Eso abría el detalle y leía un pak durante el arranque. Ahora no hay nada seleccionado hasta que el usuario elige una canción.
- **Ruido de CUE4Parse en el log.** Registraba "Texture2D read incorrectly" en todas las portadas hechas con el creador, por el mismo motivo que los tamaños falsos de la Fase 0, aunque la imagen sale completa. Ese mensaje concreto se filtra; el resto de avisos de CUE4Parse siguen llegando al log.

**Medición:** se repitió con Fuser abierto en el equipo, que ocupaba ~27 % de CPU.
- La lista aparece en ~1,6 s tanto con esta versión como con la anterior, así que el reproductor no añade tiempo de arranque.
- Las cifras de 0,65–0,8 s de la Fase 4 se midieron sin el juego abierto.

**Pendiente:**
- Probarlo con DLC parcheados.
- Escucharlo a mano, porque las pruebas automáticas no oyen.

## 5. Orden de trabajo y dependencias

```
Fase 0 (spike) ──► Fase 1 (núcleo) ──► Fase 2 (archivos) ──► Fase 4
                         │                                    ▲
                         └──────► Fase 3 (UI) ────────────────┘
```

- La Fase 0 es **bloqueante**: no conviene escribir `CueSongMetadataReader` ni el esquema de `extra_json` antes de conocer su resultado.
- La Fase 3 puede empezar en cuanto existan los modelos y las interfaces de la Fase 1, usando datos falsos, en paralelo con la Fase 2.
- Para cada fase, primero se completa el comando de Tools equivalente y después la UI. Así cada capa se prueba sin depender de la interfaz.

---

## 6. Riesgos específicos del MVP

| Riesgo | Mitigación |
|---|---|
| CUE4Parse no monta un pak suelto o no reconoce las clases de Harmonix | Plan B: portar `get_metadata` a C# detrás de `ISongMetadataReader` |
| Avalonia 12 es reciente y su `DataGrid` puede cambiar de paquete o de API | **Cerrado:** `Avalonia.Controls.DataGrid` 12.1.2 funciona con Avalonia 12.1.4. Su tema se incluye desde `avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml` |
| No hay paks de muestra suficientes para la paridad | Conseguirlos antes de empezar la Fase 0; es la primera tarea |
| Bloqueo de archivos por OneDrive (el repositorio está en OneDrive) | Mantener los paks de muestra y las carpetas de prueba fuera de OneDrive |
| Licencias de CUE4Parse, SharpCompress y Avalonia | **Cerrado:** todas son permisivas; avisos en `tools/THIRD-PARTY-NOTICES.txt` |
