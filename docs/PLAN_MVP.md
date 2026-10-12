# Fuser Song Manager v2 — Plan de implementación del MVP

> Complementa `FUSER_SONG_MANAGER_PROYECTO.md` (arquitectura) y `FUSER_REQUISITOS_FUNCIONALES.md` (requisitos). Este documento define **qué entra en el MVP, en qué orden se construye y cuándo se da por terminado**.

| Campo | Valor |
|---|---|
| Estado | Borrador |
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

1. Un usuario de la v1 abre la v2 por primera vez y ve su biblioteca con rating, notas y autor intactos, sin haber configurado nada a mano más que confirmar la carpeta.
2. Los siete campos coinciden con la v1 en todos los paks de muestra.
3. Con la biblioteca indexada, la lista aparece en menos de 1 segundo, y un arranque sin cambios no abre ningún pak.
4. La ventana sigue respondiendo durante el escaneo y la importación.
5. Un pak corrupto aparece marcado en la lista y no detiene el escaneo.
6. Las operaciones de habilitar, deshabilitar e importar nunca dejan un `.pak` sin su `.sig`, y se bloquean con el juego abierto.
7. Mover un pak a mano entre carpetas se refleja como cambio de estado en el siguiente arranque, sin perder datos.
8. Buscar y ordenar se perciben como instantáneos con al menos 2000 canciones.

---

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
| Avalonia 12 es reciente y su `DataGrid` puede cambiar de paquete o de API | Comprobar en la Fase 3 el paquete `Avalonia.Controls.DataGrid` compatible; como alternativa, usar `ListBox` virtualizado con plantilla de fila |
| No hay paks de muestra suficientes para la paridad | Conseguirlos antes de empezar la Fase 0; es la primera tarea |
| Bloqueo de archivos por OneDrive (el repositorio está en OneDrive) | Mantener los paks de muestra y las carpetas de prueba fuera de OneDrive |
| Licencias de CUE4Parse, SharpCompress y Avalonia | Comprobarlas antes de la primera publicación |
