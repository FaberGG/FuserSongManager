# Fuser Song Manager v2 — Documento de requisitos funcionales

> Complementa el documento `FUSER_SONG_MANAGER_PROYECTO.md` (contexto, problema, decisiones de tecnología y arquitectura). Este documento define **qué debe hacer** el sistema. El cómo está en el documento de arquitectura.

| Campo | Valor |
|---|---|
| Estado | Borrador para revisión |
| Fecha | 12 de octubre de 2026 |
| Base | Fuser Custom Song Manager v1.1.3 (paridad funcional) + mejoras acordadas |
| Usuarios | Jugadores de *Fuser* que instalan y gestionan canciones custom |

---

## 1. Cómo leer este documento

### 1.1 Estado de cada requisito

| Estado | Significado |
|---|---|
| **Confirmado** | Acordado en la conversación o presente en la v1; se implementa |
| **Por validar** | Depende de una verificación técnica previa (prototipo de la Fase 0) |
| **Pendiente de viabilidad** | Idea deseada cuya factibilidad aún no se ha investigado |

### 1.2 Prioridad

| Prioridad | Significado |
|---|---|
| **Debe** | Imprescindible para la v2.0 |
| **Debería** | Importante, entra en la v2.0 si el tiempo lo permite |
| **Podría** | Deseable, para versiones posteriores |

### 1.3 Convención de identificadores

`RF-<ÁREA>-<NN>`. Las áreas son: BIB (biblioteca), MET (metadatos), EST (estado de canciones), IMP (importación), USR (datos del usuario), BUS (búsqueda y filtros), DET (detalle), CFG (configuración), UI (interfaz), SIS (sistema) y PEN (pendientes de viabilidad).

### 1.4 Origen de los requisitos

Los requisitos de paridad salen del código de la v1 que se revisó (`loader.py`, `song_extract.py`, `manager_classes.py`, `program_enums.py`). Donde la v1 tiene comportamiento que **no se pudo ver completo** (por ejemplo, el detalle de `manager.py`), el requisito se marca como **Por validar** en vez de asumirlo.

---

## 2. Glosario mínimo

| Término | Significado |
|---|---|
| Pak / Sig | Pareja de archivos de una canción (`<nombre>_P.pak` y `<nombre>_P.sig`) |
| Habilitada | Canción en `custom_songs`, visible para el juego |
| Deshabilitada | Canción en `disabled_songs`, ignorada por el juego |
| DLC parcheado | Canción oficial cuyos metadatos se toman de un catálogo propio, no del pak |
| Crate | Colección de canciones que el jugador arma dentro de *Fuser* |
| Propiedades adicionales | Datos de los assets del pak que la v1 descarta y la v2 podría mostrar |

---

## 3. Requisitos funcionales confirmados

### 3.1 Biblioteca y sincronización (BIB)

| ID | Requisito | Prioridad | Estado |
|---|---|---|---|
| RF-BIB-01 | El sistema debe detectar todos los `.pak` de las carpetas de canciones habilitadas y deshabilitadas, incluyendo subcarpetas. | Debe | Confirmado |
| RF-BIB-02 | Al iniciar, el sistema debe mostrar de inmediato la biblioteca guardada en la base de datos, sin esperar al escaneo. | Debe | Confirmado |
| RF-BIB-03 | El escaneo debe ejecutarse en segundo plano, sin bloquear la interfaz. | Debe | Confirmado |
| RF-BIB-04 | La sincronización debe ser incremental: solo se leen los paks nuevos o modificados (por tamaño o fecha de modificación). | Debe | Confirmado |
| RF-BIB-05 | Los paks se deben leer en paralelo, con un grado de paralelismo configurable. | Debe | Confirmado |
| RF-BIB-06 | El usuario debe poder cancelar un escaneo en curso. | Debería | Confirmado |
| RF-BIB-07 | El sistema debe mostrar el progreso del escaneo (por ejemplo, "120 de 480"). | Debería | Confirmado |
| RF-BIB-08 | Las canciones que ya no existen en disco deben eliminarse de la base de datos, **conservando** rating, notas y autor por si el archivo reaparece. | Debería | Por validar |
| RF-BIB-09 | El usuario debe poder forzar un reescaneo completo de la biblioteca. | Debería | Confirmado |
| RF-BIB-10 | Si una canción cambia de carpeta fuera del manager (se movió a mano), el sistema debe detectarlo y actualizar su estado sin perder los datos del usuario. | Debe | Confirmado |

**Criterios de aceptación (ejemplos)**
- RF-BIB-02: con una biblioteca ya indexada, la lista aparece antes de que termine cualquier lectura de pak.
- RF-BIB-04: si no cambió ningún archivo desde el último arranque, no se abre ningún pak.
- RF-BIB-10: mover un pak de `custom_songs` a `disabled_songs` desde el explorador de archivos, y reabrir el manager, deja la canción marcada como deshabilitada con su rating intacto.

### 3.2 Lectura de metadatos (MET)

| ID | Requisito | Prioridad | Estado |
|---|---|---|---|
| RF-MET-01 | Por cada canción, el sistema debe obtener: título, artista, año, género, tonalidad (key), modo (mayor/menor) y BPM, leyéndolos del pak. | Debe | Confirmado |
| RF-MET-02 | La lectura debe identificar cada dato **por su nombre de propiedad**, no por su posición dentro del asset. | Debe | Confirmado |
| RF-MET-03 | Para los DLC parcheados, el sistema debe tomar los metadatos del catálogo propio en lugar del pak. | Debe | Confirmado |
| RF-MET-04 | Si un pak no se puede leer, o se lee solo en parte, el sistema debe conservar la canción en la biblioteca, registrar el estado (`ok`, `parcial` o `fallido`) y el motivo, y no detener el escaneo. | Debe | Confirmado |
| RF-MET-05 | Los géneros desconocidos deben mapearse a un valor por defecto en lugar de producir un error (la v1 trata `Num` como Indie y `None` como Other). | Debe | Confirmado |
| RF-MET-06 | El sistema debe guardar las propiedades adicionales de los assets `Meta_*` que no formen parte de los siete campos principales, para poder mostrarlas en el detalle. | Debería | Por validar |
| RF-MET-07 | El sistema debe enumerar el contenido del pak (lista de assets que contiene) y guardarlo como información de la canción. | Podría | Por validar |
| RF-MET-08 | El catálogo de DLC parcheados debe poder actualizarse sin recompilar la aplicación. | Podría | Confirmado |

**Valores válidos de referencia (de la v1)**
- Géneros: Classical, Country, Rock, Latin & Caribbean, Pop, R&B, Hip Hop, Dance, Indie, Other.
- Tonalidades: C, Db, D, Eb, E, F, Gb, G, Ab, A, Bb, B, más "sin dato".
- Modos: Major, Minor, más "sin dato".

### 3.3 Estado de las canciones: habilitar y deshabilitar (EST)

| ID | Requisito | Prioridad | Estado |
|---|---|---|---|
| RF-EST-01 | El usuario debe poder habilitar o deshabilitar una canción; el sistema mueve el `.pak` y el `.sig` juntos entre las carpetas correspondientes. | Debe | Confirmado |
| RF-EST-02 | Al mover una canción, el sistema debe preservar la estructura de subcarpetas. | Debe | Confirmado |
| RF-EST-03 | El usuario debe poder habilitar o deshabilitar varias canciones a la vez (selección múltiple). | Debe | Confirmado |
| RF-EST-04 | Antes de habilitar una canción, el sistema debe verificar que exista su `.sig`. Si falta, la canción permanece deshabilitada y se informa al usuario con el nombre del archivo faltante. | Debe | Confirmado |
| RF-EST-05 | Si un movimiento de archivos falla a medias, el sistema debe revertirlo para no dejar un `.pak` sin su `.sig` ni al revés. | Debe | Confirmado |
| RF-EST-06 | Si ya existe un archivo con el mismo nombre en el destino, el sistema no debe sobrescribirlo sin avisar. | Debe | Confirmado |
| RF-EST-07 | Al cambiar el estado de una canción, el sistema debe borrar `customSongsUnlocked_P.pak` y su `.sig` si existen, para que el juego los regenere. | Debería | Confirmado (la v1 lo hace al refrescar y al lanzar) |
| RF-EST-08 | El sistema debe avisar o impedir la operación si *Fuser* está en ejecución. | Debería | Confirmado (la v1 lo impide con `tasklist`) |

**Notas de validación**
- RF-EST-07 (resuelto): la v1 borra el caché en `refresh_list`, en `launch_fuser` y al mover canciones (`manager.py`). La v2 lo hace tras cada cambio de estado o importación y antes de lanzar.
- RF-EST-08 (resuelto): la v1 usa esos nombres de proceso (`Fuser-Win64-Shipping.exe` y `FuserEOS-Win64-Shipping.exe`) para bloquear cambios de estado e importaciones mientras el juego está abierto. La v2 hace lo mismo.

### 3.4 Importación de canciones (IMP)

| ID | Requisito | Prioridad | Estado |
|---|---|---|---|
| RF-IMP-01 | El usuario debe poder importar canciones desde archivos `.zip`, `.rar` y `.7z`. | Debe | Confirmado |
| RF-IMP-02 | Un archivo comprimido puede contener una o varias canciones, en cualquier nivel de carpetas; el sistema las debe encontrar todas. | Debe | Confirmado |
| RF-IMP-03 | Solo se deben importar los `.pak` que tengan su `.sig` con el mismo nombre. Los incompletos se informan al usuario. | Debe | Confirmado |
| RF-IMP-04 | El usuario debe poder elegir si las canciones importadas quedan habilitadas o deshabilitadas. | Debe | Confirmado |
| RF-IMP-05 | Si la canción ya existe, el sistema debe respetar su ubicación actual y no crear un duplicado en la carpeta contraria. | Debe | Confirmado |
| RF-IMP-06 | El sistema debe permitir importar arrastrando y soltando archivos comprimidos sobre la ventana. | Debería | Confirmado |
| RF-IMP-07 | La importación debe mostrar progreso y no bloquear la interfaz. | Debe | Confirmado |
| RF-IMP-08 | Al terminar, el sistema debe mostrar un resumen: canciones importadas, omitidas y errores. | Debería | Confirmado |
| RF-IMP-09 | El sistema debe limpiar siempre los archivos temporales, incluso si la importación falla. | Debe | Confirmado |
| RF-IMP-10 | El usuario debe poder importar varios archivos comprimidos en una sola operación. | Podría | Confirmado |

### 3.5 Datos propios del usuario (USR)

| ID | Requisito | Prioridad | Estado |
|---|---|---|---|
| RF-USR-01 | El usuario debe poder asignar una valoración (rating) a cada canción. | Debe | Confirmado |
| RF-USR-02 | El usuario debe poder escribir notas libres por canción. | Debe | Confirmado |
| RF-USR-03 | El usuario debe poder registrar el autor del pak (quien creó la canción custom). | Debe | Confirmado |
| RF-USR-04 | Estos datos deben persistir en la base de datos local entre sesiones. | Debe | Confirmado |
| RF-USR-05 | Al actualizar desde la v1, el sistema debe importar la base de datos existente sin perder rating, notas ni autor. | Debe | Confirmado |
| RF-USR-06 | El usuario debe poder editar rating, notas y autor de varias canciones a la vez. | Podría | Confirmado |

### 3.6 Búsqueda, filtros y ordenamiento (BUS)

| ID | Requisito | Prioridad | Estado |
|---|---|---|---|
| RF-BUS-01 | El usuario debe poder buscar por texto en título, artista, autor y notas. | Debe | Confirmado |
| RF-BUS-02 | El usuario debe poder filtrar por género, tonalidad, modo, rango de BPM, rango de años, estado (habilitada o deshabilitada), rating y autor. | Debe | Confirmado |
| RF-BUS-03 | Los filtros deben poder combinarse y limpiarse con una sola acción. | Debe | Confirmado |
| RF-BUS-04 | El usuario debe poder ordenar por cualquier columna. | Debe | Confirmado |
| RF-BUS-05 | Al ordenar por título o artista, el sistema debe ignorar el prefijo inicial "The " y "A ", igual que el juego. | Debe | Confirmado |
| RF-BUS-06 | El resultado debe actualizarse al instante al escribir o cambiar un filtro, incluso con miles de canciones. | Debe | Confirmado |
| RF-BUS-07 | El sistema debe mostrar cuántas canciones coinciden con la búsqueda actual. | Debería | Confirmado |
| RF-BUS-08 | El usuario debe poder guardar combinaciones de filtros con un nombre y reutilizarlas. | Podría | Confirmado |
| RF-BUS-09 | La búsqueda y los filtros deben poder usarse sobre las propiedades adicionales, si se confirma que existen. | Podría | Por validar |

> **Por qué importa:** *Fuser* no ofrece búsqueda ni filtros por metadatos. Esta área es una de las que más valor aporta y es la base de los requisitos de crates de la sección 4.

### 3.7 Detalle de canción (DET)

| ID | Requisito | Prioridad | Estado |
|---|---|---|---|
| RF-DET-01 | Al seleccionar una canción, el sistema debe mostrar un panel con todos sus datos: metadatos del juego, estado, ubicación, rating, notas y autor. | Debe | Confirmado |
| RF-DET-02 | El panel debe indicar el estado de lectura de la canción y el motivo si fue parcial o fallida. | Debe | Confirmado |
| RF-DET-03 | El panel debe mostrar las propiedades adicionales descubiertas, en una sección separada. | Debería | Por validar |
| RF-DET-04 | El panel debe mostrar el contenido del pak (lista de assets). | Podría | Por validar |
| RF-DET-05 | El panel debe permitir abrir la carpeta que contiene el pak en el explorador de archivos. | Debería | Confirmado |

### 3.8 Configuración (CFG)

| ID | Requisito | Prioridad | Estado |
|---|---|---|---|
| RF-CFG-01 | El usuario debe poder indicar la carpeta de instalación de *Fuser*; el sistema deriva de ahí las carpetas de canciones habilitadas y deshabilitadas. | Debe | Confirmado |
| RF-CFG-02 | En el primer arranque, el sistema debe guiar al usuario para configurar la carpeta y crear las carpetas que falten. | Debe | Confirmado |
| RF-CFG-03 | El sistema debe validar que la carpeta indicada parezca una instalación de *Fuser* y avisar si no. | Debería | Confirmado |
| RF-CFG-04 | El usuario debe poder indicar el tipo de lanzador (Steam, Epic o local) y, si hace falta, la ruta del ejecutable. | Debería | Por validar |
| RF-CFG-05 | El usuario debe poder lanzar *Fuser* desde el manager. | Podría | Por validar |
| RF-CFG-06 | El usuario debe poder ajustar el grado de paralelismo del escaneo. | Podría | Confirmado |
| RF-CFG-07 | El usuario debe poder elegir tema claro u oscuro. | Debería | Confirmado |
| RF-CFG-08 | El sistema debe recordar el tamaño de ventana, columnas visibles y orden elegido. | Podría | Confirmado |

### 3.9 Interfaz (UI)

| ID | Requisito | Prioridad | Estado |
|---|---|---|---|
| RF-UI-01 | La lista de canciones debe estar virtualizada: debe responder con fluidez con miles de elementos. | Debe | Confirmado |
| RF-UI-02 | Ninguna operación larga (escaneo, importación, movimiento por lotes) debe congelar la ventana. | Debe | Confirmado |
| RF-UI-03 | El usuario debe poder elegir qué columnas ver y reordenarlas. | Debería | Confirmado |
| RF-UI-04 | Las canciones con problemas (fallo de lectura, falta de `.sig`) deben distinguirse visualmente en la lista. | Debe | Confirmado |
| RF-UI-05 | Las acciones destructivas o masivas deben pedir confirmación. | Debe | Confirmado |
| RF-UI-06 | Los mensajes de error deben decir qué pasó y qué archivo está afectado, en lenguaje claro. | Debe | Confirmado |
| RF-UI-07 | La interfaz debe poder usarse con teclado para las acciones frecuentes (buscar, habilitar o deshabilitar, calificar). | Podría | Confirmado |

### 3.10 Sistema (SIS)

| ID | Requisito | Prioridad | Estado |
|---|---|---|---|
| RF-SIS-01 | El sistema debe registrar sus eventos y errores en un archivo de log consultable por el usuario. | Debería | Confirmado |
| RF-SIS-02 | El sistema debe comprobar si hay una versión nueva y avisar al usuario sin interrumpirlo. | Debería | Confirmado |
| RF-SIS-03 | El sistema debe migrar el esquema de la base de datos automáticamente al actualizar. | Debe | Confirmado |
| RF-SIS-04 | El usuario debe poder exportar la información de su biblioteca (rating, notas, autor y estado) como copia de seguridad, e importarla de nuevo. | Podría | Confirmado |
| RF-SIS-05 | Una herramienta de diagnóstico debe permitir volcar todas las propiedades de un pak para investigar qué datos contiene. | Debería | Confirmado |

---

## 4. Requisitos pendientes de viabilidad (PEN)

Esta sección recoge ideas que se quieren, pero cuya factibilidad técnica **no se ha investigado**. Se dejan redactadas para no perderlas, y cada una indica qué falta averiguar.

### 4.1 Crates de canciones

**Problema que resuelve.** Dentro de *Fuser* se pueden crear crates con canciones seleccionadas, pero el juego no tiene búsqueda ni filtros avanzados por metadatos. Con bibliotecas grandes, elegir qué canciones agregar a un crate es lento y poco preciso.

**Lo que se sabe y lo que no.** Se sabe que el juego permite crear crates. **No se sabe** dónde ni cómo el juego guarda los crates (si en un archivo de guardado, en la nube de la plataforma, o en otro formato), ni si es seguro o posible leerlos o escribirlos desde fuera. Esto determina qué versión de la funcionalidad es viable.

Se plantean tres niveles, de menor a mayor dependencia del juego. Cada nivel es útil por sí mismo.

#### Nivel A — Selecciones dentro del manager (no depende del juego)

| ID | Requisito | Prioridad | Estado |
|---|---|---|---|
| RF-PEN-01 | El usuario debe poder crear colecciones propias del manager (por ejemplo, "Para la sesión del sábado"), con nombre y descripción. | Debería | Pendiente de viabilidad |
| RF-PEN-02 | El usuario debe poder agregar canciones a una colección desde la lista filtrada, de forma individual o en lote (por ejemplo, "todas las de 120 a 128 BPM en La menor"). | Debería | Pendiente de viabilidad |
| RF-PEN-03 | Una colección debe mostrar sus canciones con los mismos metadatos, orden y filtros que la biblioteca. | Debería | Pendiente de viabilidad |
| RF-PEN-04 | El usuario debe poder exportar una colección como lista de texto legible (título y artista) para tenerla a mano al armar el crate dentro del juego. | Debería | Pendiente de viabilidad |
| RF-PEN-05 | El usuario debe poder habilitar solo las canciones de una colección y deshabilitar el resto. | Podría | Pendiente de viabilidad |

Este nivel **no requiere** conocer el formato de crates del juego y ya resuelve gran parte del problema: el filtrado se hace en el manager y el usuario solo traslada la selección al juego.

#### Nivel B — Lectura de los crates existentes del juego

| ID | Requisito | Prioridad | Estado |
|---|---|---|---|
| RF-PEN-06 | El sistema debe poder leer los crates que el jugador ya creó en *Fuser* y mostrarlos en el manager. | Podría | Pendiente de viabilidad |
| RF-PEN-07 | Al ver un crate, el sistema debe indicar qué canciones custom contiene y cuáles ya no están instaladas. | Podría | Pendiente de viabilidad |

**Por investigar:** ubicación del archivo de guardado en cada plataforma (Steam y Epic), formato, si está cifrado o firmado, y si el almacenamiento en la nube de la plataforma lo sobrescribe.

#### Nivel C — Crear o modificar crates en el juego desde el manager

| ID | Requisito | Prioridad | Estado |
|---|---|---|---|
| RF-PEN-08 | El sistema debe poder crear un crate en *Fuser* a partir de una colección del manager. | Podría | Pendiente de viabilidad |
| RF-PEN-09 | Antes de modificar cualquier archivo de guardado del juego, el sistema debe hacer una copia de seguridad automática y permitir restaurarla. | Debe (si se implementa) | Pendiente de viabilidad |
| RF-PEN-10 | El sistema debe negarse a modificar datos del juego si *Fuser* está en ejecución. | Debe (si se implementa) | Pendiente de viabilidad |

**Riesgos principales de este nivel:** el formato podría no estar documentado, los guardados podrían estar firmados o cifrados, una modificación incorrecta podría corromper el progreso del jugador, y una actualización del juego podría romper la compatibilidad. Por eso se exige copia de seguridad y avisos explícitos si llega a implementarse. **Antes de comprometerlo, hace falta una investigación específica** (ver 4.3).

### 4.2 Otras ideas por evaluar

| ID | Idea | Qué falta averiguar |
|---|---|---|
| RF-PEN-11 | Etiquetas personalizadas por canción (por ejemplo, "calentamiento" o "para cerrar"), con filtro por etiqueta. | Nada técnico; solo decidir si se prefiere frente a las colecciones |
| RF-PEN-12 | Detección de canciones duplicadas (mismo título y artista en paks distintos). | Criterio de coincidencia y qué hacer con los duplicados |
| RF-PEN-13 | Previsualización de audio de la canción desde el manager. | Si los stems del pak se pueden decodificar con CUE4Parse y en qué formato están; derechos y tamaño de los archivos |
| RF-PEN-14 | Mostrar la portada o imagen de la canción, si el pak la incluye. | Si los paks contienen imágenes y en qué formato |
| RF-PEN-15 | Estadísticas de la biblioteca (por género, década, BPM, tonalidad). | Nada técnico; depende de que los metadatos sean fiables |
| RF-PEN-16 | Compatibilidad de mezcla: sugerir canciones con tonalidad y BPM compatibles con una canción dada. | Reglas musicales deseadas (círculo de quintas, tolerancia de BPM) y cómo trata el juego la transposición |
| RF-PEN-17 | Generar el archivo `.sig` cuando falte. | Si es posible sin las claves del juego; hoy está fuera de alcance |
| RF-PEN-18 | Verificación de integridad de paks (detectar archivos corruptos antes de que el juego falle). | Qué comprobaciones son posibles con CUE4Parse |

### 4.3 Tareas de investigación para cerrar la viabilidad

| # | Tarea | Resultado esperado | Desbloquea |
|---|---|---|---|
| 1 | Localizar en disco dónde guarda *Fuser* los crates (Steam y Epic), con el juego cerrado y comparando antes y después de crear un crate de prueba | Ruta y tipo de archivo | RF-PEN-06 a RF-PEN-10 |
| 2 | Examinar el archivo: ¿formato legible, cifrado, firmado, sincronizado con la nube? | Veredicto de lectura y escritura | Niveles B y C |
| 3 | Revisar si la comunidad de modding ya documentó ese formato | Documentación o herramientas reutilizables | Niveles B y C |
| 4 | Con la herramienta de diagnóstico (RF-SIS-05), volcar varios paks y listar los assets y propiedades | Inventario real de datos disponibles | RF-MET-06, RF-MET-07, RF-DET-03, RF-DET-04, RF-PEN-13, RF-PEN-14 |
| 5 | Confirmar el comportamiento de la v1 en `manager.py` (lanzador, procesos del juego, archivo `customSongsUnlocked_P`) | Lista de comportamientos a replicar | RF-EST-07, RF-EST-08, RF-CFG-04, RF-CFG-05 |

---

## 5. Requisitos no funcionales relacionados

Los detalles de implementación están en el documento de arquitectura; aquí se resumen solo los que condicionan los requisitos funcionales.

| ID | Requisito |
|---|---|
| RNF-01 | Con una biblioteca ya indexada, la lista debe aparecer en menos de un segundo. |
| RNF-02 | La interfaz debe responder a filtros y búsquedas en tiempo percibido como instantáneo con bibliotecas de miles de canciones. |
| RNF-03 | Un pak corrupto o inesperado nunca debe cerrar la aplicación ni detener un escaneo. |
| RNF-04 | Una operación de archivos nunca debe dejar un `.pak` separado de su `.sig`. |
| RNF-05 | El manager nunca debe modificar ni borrar archivos fuera de las carpetas de canciones y de su propia carpeta de datos, salvo que el usuario lo autorice de forma explícita (por ejemplo, en un eventual Nivel C de crates). |
| RNF-06 | Los datos del usuario (rating, notas, autor, colecciones) deben sobrevivir a actualizaciones de la aplicación. |

---

## 6. Trazabilidad con el documento de arquitectura

| Área funcional | Componente de arquitectura |
|---|---|
| Biblioteca y sincronización (BIB) | `LibraryScanner`, `LibrarySyncService` |
| Metadatos (MET) | `ISongMetadataReader` / `CueSongMetadataReader`, `PatchedDlcCatalog` |
| Estado de canciones (EST) | `SongStateService` |
| Importación (IMP) | `IArchiveImporter` / `SharpCompressImporter` |
| Datos del usuario (USR) | `ISongRepository` / `SqliteSongRepository` |
| Búsqueda y filtros (BUS), Detalle (DET), UI | `LibraryViewModel`, `SongDetailViewModel` |
| Configuración (CFG) | `SettingsViewModel`, archivo de configuración JSON |
| Sistema (SIS) | Logging, migraciones, proyecto `FuserSongManager.Tools` |
| Pendientes (PEN) | Se definirá un componente nuevo (por ejemplo, `CollectionService`) si el Nivel A se aprueba |

---

## 7. Resumen de alcance propuesto por versión

| Versión | Contenido |
|---|---|
| **v2.0** | Todos los requisitos **Debe** confirmados: biblioteca, metadatos de los siete campos, habilitar y deshabilitar, importación, datos del usuario, búsqueda y filtros, detalle, configuración, interfaz |
| **v2.1** | Requisitos **Debería** y los **Por validar** que la Fase 0 confirme (propiedades adicionales, panel de detalle ampliado) |
| **v2.x** | Colecciones del manager (Nivel A de crates) y las ideas de la sección 4.2 que se aprueben |
| **Por decidir** | Niveles B y C de crates, solo si la investigación 4.3 demuestra que es viable y seguro |

---

## 8. Decisiones abiertas para el usuario

1. **Crates:** ¿se aprueba empezar por el Nivel A (colecciones dentro del manager) mientras se investiga el formato del juego?
2. **Rating:** ¿qué escala se quiere (la v1 usa estrellas; se debe confirmar el máximo)?
3. **Autor:** ¿se mantiene como texto libre o se quiere una lista de autores reutilizable?
4. **Canciones borradas del disco:** ¿se conservan los datos del usuario (RF-BIB-08) o se eliminan junto con la fila?
5. **Lanzar el juego:** ¿se quiere ese comportamiento en la v2.0 o se deja para después?
