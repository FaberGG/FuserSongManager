# Fuser Song Manager v2 — Documento de definición del proyecto

> Documento de referencia para definir la migración del *Fuser Custom Song Manager* (Python/Tkinter) a una aplicación de escritorio en C#/.NET con Avalonia UI y CUE4Parse.

| Campo | Valor |
|---|---|
| Estado | Borrador para definición |
| Fecha | 12 de octubre de 2026 |
| Proyecto base | Fuser Custom Song Manager v1.1.3 (Python) |
| Stack objetivo | C# / .NET, Avalonia UI, CUE4Parse, SQLite |

---

## 1. Contexto

### 1.1 El juego y las canciones custom

*Fuser* es un videojuego de mezcla musical hecho con Unreal Engine. La comunidad crea canciones custom que se distribuyen como un par de archivos:

- `<nombre>_P.pak`: paquete de Unreal Engine con los assets de la canción.
- `<nombre>_P.sig`: archivo de firma que el juego requiere para cargar el pak.

Los archivos se instalan en `Fuser/Content/Paks/custom_songs`. Para desactivar una canción sin borrarla, el manager actual la mueve a `Fuser/Content/Paks/disabled_songs`.

### 1.2 El manager actual

El proyecto existente es una aplicación de escritorio en Python con Tkinter. Sus responsabilidades son:

1. Escanear las carpetas de canciones habilitadas y deshabilitadas.
2. Leer los metadatos de cada `.pak`.
3. Persistir el estado en SQLite (habilitada, rating, notas, autor).
4. Habilitar y deshabilitar canciones moviendo archivos entre carpetas.
5. Importar canciones desde archivos comprimidos (`.zip`, `.rar`, `.7z`).
6. Mostrar, ordenar y buscar la biblioteca en una tabla.

### 1.3 Cómo funciona hoy la lectura de metadatos

Cada `.pak` contiene dos assets de metadatos que el manager lee:

| Asset dentro del pak | Datos que se extraen |
|---|---|
| `DLC/Songs/{nombre}/Meta_{nombre}.uasset` + `.uexp` | Artista, género, año |
| `Audio/Songs/{nombre}/{nombre}bs/Meta_{nombre}bs.uasset` + `.uexp` | Título, BPM, tonalidad (key), modo (mayor/menor) |

El flujo actual es:

1. `PyPAKParser` abre el `.pak` y extrae los bytes de los cuatro archivos anteriores.
2. Una función propia (`get_metadata`) implementa a mano el formato binario UAsset/UExp con `struct.unpack`: lee la cabecera, la tabla de nombres, imports y exports, y luego recorre las properties (`NameProperty`, `TextProperty`, `EnumProperty`, `IntProperty`, `ArrayProperty`, `StructProperty`, entre otras).
3. Los valores finales se toman por **índices fijos** de la lista resultante (por ejemplo `data_final[3][3][2][0][0]` para el artista).
4. Las canciones DLC oficiales que ya vienen parcheadas en el juego no se parsean: sus metadatos salen de una lista precargada (`patched_dlc_metadata.json`).

Esta lógica de parseo se debe al trabajo de ingeniería inversa de la comunidad (el código acredita a NarrikSynthfox) y es el conocimiento de dominio más valioso del proyecto actual.

---

## 2. Problema

### 2.1 Limitaciones de rendimiento

- **Escaneo totalmente secuencial.** No hay hilos, procesos ni `async` en el código. Cada `.pak` se abre y se parsea uno tras otro, y el tiempo de arranque crece linealmente con el tamaño de la biblioteca.
- **Búsqueda de archivos ineficiente.** `get_folder_path_to_file` ejecuta un `glob` recursivo sobre todo el directorio por cada archivo que busca, lo que produce un comportamiento cuadrático cuando hay muchas canciones.
- **Parser en Python puro.** Leer estructuras binarias byte a byte con `struct.unpack` es costoso frente a un parser compilado.

### 2.2 Limitaciones de la interfaz

- Tkinter ofrece pocos controles y una apariencia limitada.
- La tabla (`ttk.Treeview` extendida) no está virtualizada y tiene ordenamiento manual propio.
- Las operaciones largas (escanear, extraer archivos comprimidos) bloquean la interfaz porque no hay concurrencia.
- Hacer interfaces más ricas (paneles de detalle, previsualización, filtros avanzados, temas) resulta impráctico.

### 2.3 Fragilidad del parser

- Los índices fijos asumen que Harmonix siempre serializa las properties en el mismo orden. Si un pak llega con otro layout, el resultado es erróneo o falla con una `AssertionError` genérica.
- El parser solo cubre un subconjunto de tipos de property y descarta el resto.
- El parser se detiene al llegar a `IsStreamOptimized` y la rama de `mReferencedChildAssets` (`HmxMidiFileAsset`) solo imprime información de depuración. Hay datos ya parseados o fácilmente parseables que no se aprovechan.

### 2.4 Metadatos sin explotar

Hoy solo se recopilan siete campos del juego (título, artista, año, género, key, modo, BPM). El código indica que existe más información accesible:

- Properties del `.uexp` que se leen y se descartan.
- Referencias a assets hijos (archivos MIDI, entre otros).
- El resto del contenido del `.pak`, que ni siquiera se enumera (stems de audio, imágenes, otros assets).

Qué contiene realmente cada pak es una **hipótesis por validar** (ver sección 7).

### 2.5 Deuda técnica adicional detectada

- Rutas con separadores de Windows escritos a mano (`"\\"`), lo que dificulta la portabilidad y las pruebas.
- Consultas SQL construidas con f-strings (`f"DELETE FROM songs WHERE filename = '{song_item}'"`), vulnerables a nombres de archivo con comillas.
- Lógica de negocio, acceso a archivos y GUI mezcladas en `manager.py`, un archivo muy grande.
- Lista de DLC parcheados mantenida a mano en el código.

### 2.6 Enunciado del problema

> La comunidad necesita un gestor de canciones custom de *Fuser* que escale a bibliotecas grandes sin bloquearse, que lea los metadatos de forma robusta (sin depender de offsets hardcodeados), que exponga más información de cada canción y que permita construir una interfaz moderna y extensible.

---

## 3. Objetivos y alcance

### 3.1 Objetivos

1. Reemplazar el parser artesanal por una librería madura de parseo de Unreal Engine.
2. Escanear la biblioteca en paralelo y de forma incremental (solo reparsear lo que cambió).
3. Ofrecer una GUI moderna con tabla virtualizada, filtros, panel de detalle y operaciones no bloqueantes.
4. Mantener la paridad funcional con la versión actual.
5. Descubrir y exponer metadatos adicionales de los archivos.
6. Separar la lógica de negocio de la interfaz para poder probarla de forma independiente.

### 3.2 Paridad funcional mínima (v2.0)

- Detección de canciones habilitadas y deshabilitadas.
- Habilitar y deshabilitar canciones (mover `.pak` y `.sig` juntos).
- Validación de la existencia del `.sig`.
- Importación desde `.zip`, `.rar` y `.7z`.
- Rating, notas y autor por canción.
- Búsqueda y ordenamiento (incluyendo la regla de ignorar "The " y "A " al ordenar títulos y artistas).
- Configuración del directorio de Fuser y del tipo de lanzador (Steam, Epic o local).
- Soporte de las subcarpetas dentro de `custom_songs`.
- Migración de la base de datos SQLite existente (rating, notas, autor).

### 3.3 Fuera de alcance de la v2.0

- Edición o creación de paks.
- Generación de archivos `.sig`.
- Soporte de plataformas distintas de Windows (se deja la puerta abierta, no se implementa).
- Reproducción de audio (candidato para una fase posterior).

---

## 4. Solución propuesta

Reescribir el manager como una aplicación de escritorio **C#/.NET con Avalonia UI**, usando **CUE4Parse** para leer los paks y assets de Unreal Engine, y manteniendo **SQLite** como almacén del estado propio del manager.

La solución se organiza en tres ideas centrales:

1. **Parseo delegado a CUE4Parse.** Se reemplazan `PyPAKParser` y `get_metadata` por una librería que ya interpreta todas las properties de un asset.
2. **Pipeline de escaneo paralelo e incremental.** Un servicio compara el sistema de archivos con la base de datos y solo parsea los paks nuevos o modificados, en paralelo y con reporte de progreso.
3. **Arquitectura por capas con MVVM.** Núcleo sin dependencias de interfaz, y una capa de presentación reactiva.

---

## 5. Decisiones de tecnología

Cada decisión incluye alternativas consideradas y el motivo de la elección.

### 5.1 Lenguaje y plataforma: C# / .NET

- **Decisión:** C# sobre .NET (versión LTS vigente al iniciar el proyecto).
- **Motivos:**
  - CUE4Parse está escrito en C#, así que se usa de forma nativa, sin puentes entre lenguajes.
  - Paralelismo maduro (`Task`, `Parallel.ForEachAsync`, `Channel<T>`).
  - Rendimiento muy superior a Python en lectura binaria.
  - Ecosistema sólido para escritorio, SQLite y archivos comprimidos.
- **Alternativas descartadas:**

| Alternativa | Motivo del descarte |
|---|---|
| Python + PySide6 | Mejora la GUI, pero el parseo sigue siendo Python puro. Es la opción de menor riesgo si se decide no reescribir. |
| Rust + Tauri | Muy buen rendimiento, pero el ecosistema de parseo UE (`repak`, `unreal_asset`) es más joven y la interfaz iría en tecnología web. |
| Electron + sidecar Python | Dos runtimes, mayor consumo de recursos y el parser lento seguiría intacto. |

### 5.2 Interfaz gráfica: Avalonia UI

- **Decisión:** Avalonia UI con patrón MVVM.
- **Motivos:**
  - Controles ricos: `DataGrid` o `ItemsRepeater` con virtualización, estilos y temas, plantillas de datos.
  - Operaciones asíncronas que no congelan la interfaz.
  - Multiplataforma por construcción, aunque el objetivo inicial sea Windows.
  - XAML declarativo con enlace de datos.
- **Alternativas consideradas:** WPF (maduro, pero solo Windows y con menos ventaja a futuro) y WinUI 3 (más ligado al ecosistema Microsoft). Si el equipo ya domina WPF, es una alternativa válida, pero la capa Core queda igual.
- **Librerías de apoyo sugeridas:**
  - `CommunityToolkit.Mvvm` para ViewModels y comandos.
  - `ReactiveUI` como alternativa si se prefiere un enfoque reactivo.

### 5.3 Parseo de paks: CUE4Parse

- **Decisión:** CUE4Parse, publicado en NuGet.
- **Motivos:**
  - Librería de parseo para paquetes y archivos de Unreal Engine 4 y 5, mantenida por el equipo de FModel.
  - Provee `DefaultFileProvider`, que monta los archivos de un directorio y permite cargar objetos y exports completos.
  - Expone todas las properties del asset, no solo siete.
  - Permite decodificar assets binarios (por ejemplo audio con `USoundWave` y texturas), lo que habilita funciones futuras.
- **Riesgos a validar** (ver sección 7): versión de UE correcta (`EGame`), comportamiento con paks de terceros montados individualmente, y mapeo de las properties de Harmonix.
- **Plan de contención:** el parser actual en Python sirve como **implementación de referencia** para comparar resultados durante las pruebas.

### 5.4 Persistencia: SQLite

- **Decisión:** mantener SQLite con `Microsoft.Data.Sqlite`, y `Dapper` como micro-ORM si se desea reducir el código repetitivo.
- **Motivos:**
  - Continuidad con la base actual, lo que permite importar rating, notas y autor de los usuarios existentes.
  - Sin servidor, un solo archivo, ideal para una aplicación de escritorio.
  - Consultas parametrizadas, lo que resuelve el problema de inyección de la v1.
- **Esquema:** se conservan las columnas actuales y se añaden las nuevas (ver sección 6.4).

### 5.5 Archivos comprimidos: SharpCompress

- **Decisión:** SharpCompress para `.zip`, `.rar` y `.7z`.
- **Motivo:** equivalente directo de `patoolib` sin depender de ejecutables externos instalados en el sistema, lo que elimina una fuente de fallos de la v1.

### 5.6 Concurrencia y flujo de datos

- **Decisión:** `Parallel.ForEachAsync` o `Channel<T>` para el pipeline de escaneo, con `IProgress<T>` para informar a la interfaz y `CancellationToken` para cancelar.
- **Motivos:** aprovechar varios núcleos en la lectura de paks, mantener la interfaz fluida y permitir cancelar un escaneo largo.
- **Ajuste de grado de paralelismo:** configurable, con un valor por defecto basado en el número de núcleos. El límite real suele ser el disco, por lo que conviene medirlo.

### 5.7 Otras decisiones transversales

| Tema | Decisión |
|---|---|
| Configuración | Archivo JSON en `%AppData%` (reemplaza el `.ini` actual) |
| Registro de eventos | `Microsoft.Extensions.Logging` con archivo rotativo (Serilog como opción) |
| Inyección de dependencias | `Microsoft.Extensions.DependencyInjection` |
| Pruebas | xUnit, con paks de ejemplo como datos de prueba |
| Distribución | Ejecutable autocontenido (`self-contained`), instalador o ZIP portable |
| Actualizaciones | Comprobación contra las versiones publicadas del proyecto (equivalente a `update_checker.py`) |

---

## 6. Arquitectura del sistema

### 6.1 Vista general por capas

```
┌──────────────────────────────────────────────────────────┐
│                  FuserSongManager.UI                     │
│          Avalonia · Views (XAML) · ViewModels            │
└───────────────────────────┬──────────────────────────────┘
                            │ depende de (interfaces)
┌───────────────────────────▼──────────────────────────────┐
│                 FuserSongManager.Core                    │
│  Servicios de aplicación · Modelos · Reglas de negocio   │
│  (sin referencias a Avalonia)                            │
└───────┬─────────────────┬─────────────────┬──────────────┘
        │                 │                 │
┌───────▼───────┐ ┌───────▼───────┐ ┌───────▼───────────┐
│ Infra.Pak     │ │ Infra.Data    │ │ Infra.Files       │
│ CUE4Parse     │ │ SQLite        │ │ FS · SharpCompress│
└───────────────┘ └───────────────┘ └───────────────────┘
```

Regla de dependencias: la UI depende del Core, el Core define interfaces, y la infraestructura las implementa. El Core nunca referencia a Avalonia ni a CUE4Parse directamente.

### 6.2 Estructura de la solución

```
FuserSongManager/
├── src/
│   ├── FuserSongManager.Core/
│   │   ├── Models/             Song, SongMetadata, Genre, MusicalKey, SongMode,
│   │   │                       ProgramSettings, LauncherType
│   │   ├── Abstractions/       ISongMetadataReader, ISongRepository,
│   │   │                       ISongFileService, IArchiveImporter
│   │   └── Services/           LibraryScanner, LibrarySyncService,
│   │                           SongStateService (habilitar/deshabilitar)
│   ├── FuserSongManager.Infrastructure/
│   │   ├── Pak/                CueSongMetadataReader, PatchedDlcCatalog
│   │   ├── Data/               SqliteSongRepository, migraciones, importador v1
│   │   └── Files/              SongFileService, SharpCompressImporter
│   ├── FuserSongManager.UI/
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Controls/
│   │   └── Assets/
│   └── FuserSongManager.Tools/    CLI de diagnóstico (volcar properties de un pak)
└── tests/
    ├── FuserSongManager.Core.Tests/
    └── FuserSongManager.Infrastructure.Tests/
```

La herramienta de diagnóstico (`Tools`) tiene un papel importante: sirve para **explorar qué contiene realmente un pak** y alimenta las decisiones sobre qué metadatos adicionales exponer.

### 6.3 Componentes principales

**`ISongMetadataReader` / `CueSongMetadataReader`**
Recibe la ruta de un `.pak` y devuelve un `SongMetadata`. Internamente monta el pak con CUE4Parse, carga los assets `Meta_*` y `Meta_*bs`, y mapea las properties **por nombre** (no por posición). Devuelve además un diccionario con las properties adicionales encontradas (`RawProperties`), que alimenta el panel de detalle. Los errores se devuelven como resultado tipado (por ejemplo `ReadResult.Failure(reason)`), no como una canción "Unknown" silenciosa.

**`PatchedDlcCatalog`**
Carga la lista de DLC parcheados y sus metadatos desde un recurso JSON incrustado, en vez de mantenerla en código. Se conserva porque esas canciones no se pueden leer del pak.

**`LibraryScanner`**
Enumera una sola vez los `.pak` de las carpetas habilitada y deshabilitada, construyendo un índice `nombre → ruta` en memoria. Esto elimina las búsquedas recursivas repetidas de la v1.

**`LibrarySyncService`**
Compara el índice del sistema de archivos con la base de datos y produce un plan de cambios: nuevas, eliminadas, cambiadas (por tamaño o fecha de modificación) y movidas. Ejecuta el parseo **solo** de lo necesario, en paralelo.

**`SongStateService`**
Habilita y deshabilita canciones moviendo el `.pak` y el `.sig` como una unidad, preservando las subcarpetas, con validación previa y reversión si falla a mitad de la operación.

**`IArchiveImporter` / `SharpCompressImporter`**
Extrae a una carpeta temporal, empareja `.pak` con `.sig` por nombre, copia al destino respetando las reglas de conflicto de la v1 y limpia lo temporal.

**`ISongRepository` / `SqliteSongRepository`**
Acceso a datos con consultas parametrizadas, transacciones por lote y migraciones de esquema versionadas.

### 6.4 Modelo de datos

Tabla `songs` (compatible con la actual, con columnas nuevas marcadas):

| Columna | Tipo | Origen |
|---|---|---|
| `filename` | TEXT, PK (sin distinguir mayúsculas) | Nombre corto del pak (`xxx_P`) |
| `song_name` | TEXT | Metadatos del juego (en la v1 la columna se llama `name`) |
| `artist` | TEXT | Metadatos del juego |
| `year` | INTEGER | Metadatos del juego |
| `genre` | INTEGER | Metadatos del juego |
| `key` | INTEGER | Metadatos del juego |
| `mode` | INTEGER | Metadatos del juego |
| `bpm` | INTEGER | Metadatos del juego |
| `enabled` | INTEGER | Estado del manager |
| `rating` | INTEGER | Usuario |
| `notes` | TEXT | Usuario |
| `author` | TEXT | Usuario |
| `relative_path` *(nuevo)* | TEXT | Ruta relativa a la carpeta habilitada o deshabilitada (subcarpetas) |
| `has_signature` *(nuevo)* | INTEGER | Si existe el `.sig` junto al pak |
| `file_size` *(nuevo)* | INTEGER | Detección de cambios (NULL = nunca leído por la v2) |
| `file_modified_utc` *(nuevo)* | TEXT | Detección de cambios (ISO 8601) |
| `read_status` *(nuevo)* | TEXT | `ok`, `partial` o `failed` |
| `read_error` *(nuevo)* | TEXT | Motivo del estado `partial` o `failed` |
| `extra_json` *(nuevo)* | TEXT | Properties adicionales descubiertas (reservada, aún sin uso) |

`genre`, `key` y `mode` usan los mismos valores numéricos que la v1. "Sin dato" se guarda como NULL, mientras que la v1 usaba `Num`.

Una tabla `schema_version` controla las migraciones. La v2 usa su propia base (`%LocalAppData%\FuserSongManager\songs.sqlite`). La base de la v1 se importa leyéndola en modo solo lectura (`V1Import`), sin modificarla, y se conservan rating, notas y autor. Las filas importadas no tienen tamaño ni fecha, así que la primera sincronización vuelve a leer sus paks.

### 6.5 Flujo de arranque y sincronización

```
1. Cargar configuración y abrir la base de datos (migrar si hace falta)
2. Mostrar la UI de inmediato con lo que ya está en la base
3. En segundo plano:
   a. LibraryScanner enumera .pak habilitados y deshabilitados
   b. LibrarySyncService calcula el plan de cambios contra la base
   c. Para cada pak nuevo o cambiado → CueSongMetadataReader (en paralelo)
   d. Los resultados se confirman por lotes en SQLite
   e. Se notifica a la UI el progreso y las canciones nuevas
4. Los pak ausentes del disco se eliminan de la base
```

Aspecto clave: la UI **no espera** al escaneo. Muestra la biblioteca guardada al instante y se actualiza de forma incremental.

### 6.6 Capa de presentación (MVVM)

| ViewModel | Responsabilidad |
|---|---|
| `MainViewModel` | Estado global, comandos de la barra de herramientas, progreso de escaneo |
| `LibraryViewModel` | Colección de canciones, filtros, búsqueda y ordenamiento |
| `SongDetailViewModel` | Detalle de la canción seleccionada y properties adicionales |
| `ImportViewModel` | Flujo de importación de archivos comprimidos |
| `SettingsViewModel` | Directorio de Fuser, lanzador, preferencias |

Elementos de interfaz previstos:

- Tabla virtualizada con columnas configurables, ordenamiento (incluyendo la regla de "The/A") y filtros por género, key, modo, rango de BPM, año y estado.
- Panel lateral de detalle con todos los metadatos y las properties adicionales.
- Acciones por lote (habilitar o deshabilitar la selección).
- Soporte de arrastrar y soltar archivos comprimidos para importar.
- Barra de progreso del escaneo, cancelable.
- Tema claro y oscuro.

### 6.7 Manejo de errores

- Un pak ilegible no detiene el escaneo: se registra con `read_status = failed` y el motivo, y se muestra en la UI con una indicación visible.
- Los movimientos de archivos se hacen de forma que no queden `.pak` sin su `.sig` ni al revés. Si la operación falla a medias, se revierte.
- Todos los errores se registran en el archivo de log con el nombre del pak afectado.

### 6.8 Estrategia de pruebas

- **Pruebas unitarias** del Core con repositorios y lectores simulados.
- **Pruebas de paridad:** una colección de paks de ejemplo se lee con el parser de la v1 y con el de la v2, y los siete campos actuales deben coincidir.
- **Pruebas de integración** del repositorio SQLite contra una base temporal.
- **Pruebas de rendimiento:** biblioteca sintética grande, midiendo tiempo de arranque y de sincronización incremental frente a la v1.

---

## 7. Riesgos y puntos por validar

| # | Riesgo o duda | Cómo se valida | Plan B |
|---|---|---|---|
| 1 | Versión de UE de Fuser y `EGame` correcto en CUE4Parse | Prototipo contra un pak real con la herramienta `Tools` | Probar versiones cercanas de `EGame`, o conservar el parser manual portado a C# |
| 2 | Que CUE4Parse monte y lea paks custom individuales (no solo la carpeta completa del juego) | Prototipo con un pak suelto | Abrir el pak con el lector de archivos de CUE4Parse y cargar los assets por ruta |
| 3 | Que los paks estén o no cifrados | El parser actual los lee sin clave, lo que sugiere que no, pero hay que confirmarlo | Proveer la clave AES si fuese necesaria |
| 4 | Que las properties de Harmonix se deserialicen como tipos genéricos | Volcar el export completo y comparar con la salida actual | Registrar un mapeo manual de propiedades para esas clases |
| 5 | Qué metadatos adicionales existen realmente (MIDI, stems, portada, dificultad, secciones) | Usar la herramienta `Tools` sobre varios paks | Reducir el alcance a los siete campos actuales |
| 6 | Que los DLC parcheados sigan sin poder leerse del pak | Probar con algunos de la lista | Mantener `PatchedDlcCatalog` |
| 7 | Rendimiento real del paralelismo (el cuello de botella puede ser el disco) | Pruebas de rendimiento con distintos grados de paralelismo | Limitar el paralelismo por defecto |
| 8 | Experiencia del equipo con C# y Avalonia | Estimar la curva de aprendizaje antes de comprometer fechas | Evaluar la alternativa de Python + PySide6 |
| 9 | Licencias de las librerías utilizadas | Revisar la licencia de CUE4Parse, Avalonia y SharpCompress antes de publicar | Sustituir la librería afectada |

---

## 8. Hoja de ruta sugerida

**Fase 0 — Validación técnica (prototipo)**
Herramienta de consola que monta un pak de Fuser con CUE4Parse, vuelca todas las properties de los assets `Meta_*` y compara con la salida del parser v1. Cierra los riesgos 1 a 5 y decide si el proyecto sigue con CUE4Parse o necesita el plan B.

**Fase 1 — Núcleo**
Modelos, `CueSongMetadataReader`, `PatchedDlcCatalog`, repositorio SQLite con migración desde la v1, y `LibraryScanner` más `LibrarySyncService`. Pruebas de paridad y de rendimiento.

**Fase 2 — Gestión de archivos**
Habilitar y deshabilitar, validación de `.sig`, importación de archivos comprimidos, subcarpetas.

**Fase 3 — Interfaz**
Ventana principal, tabla virtualizada, filtros, búsqueda, ordenamiento, panel de detalle, configuración, progreso del escaneo.

**Fase 4 — Paridad y estabilización**
Cierre de la paridad funcional con la v1, manejo de errores, registro de eventos, comprobación de actualizaciones, empaquetado.

**Fase 5 — Mejoras sobre la v1**
Metadatos adicionales descubiertos en la fase 0, acciones por lote, y funciones candidatas como previsualización de audio.

---

## 9. Criterios de éxito

- Paridad: los siete campos de metadatos actuales coinciden con la v1 en la colección de paks de prueba.
- Rendimiento: el arranque con biblioteca ya indexada muestra la lista en menos de un segundo, y la sincronización incremental solo procesa los paks que cambiaron. La mejora frente a la v1 se mide y se documenta.
- Fluidez: la interfaz permanece interactiva durante escaneos e importaciones.
- Robustez: un pak corrupto o inesperado nunca detiene el escaneo ni cierra la aplicación.
- Continuidad: un usuario de la v1 conserva rating, notas y autor al actualizar.
- Valor añadido: al menos un metadato adicional útil se expone en la interfaz, si la fase 0 confirma que existe.

---

## 10. Glosario

| Término | Significado |
|---|---|
| **Pak** | Archivo contenedor de assets de Unreal Engine (`.pak`) |
| **Sig** | Archivo de firma que acompaña al pak (`.sig`) |
| **UAsset / UExp** | Pareja de archivos de un asset serializado de Unreal Engine: cabecera con tablas de nombres e imports, y datos de las properties |
| **Property** | Campo serializado de un objeto de Unreal (texto, entero, enum, arreglo, estructura, entre otros) |
| **CUE4Parse** | Librería C# para leer paquetes y archivos de Unreal Engine 4 y 5 |
| **Avalonia UI** | Framework de interfaces de escritorio para .NET basado en XAML |
| **MVVM** | Patrón Model-View-ViewModel para separar la interfaz de la lógica |
| **DLC parcheado** | Canción oficial cuyos datos ya están integrados en el juego y no se leen de su pak |
| **Paridad funcional** | Que la nueva versión haga todo lo que hacía la anterior |
