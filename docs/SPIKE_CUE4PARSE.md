# Fase 0 — Resultado del spike con CUE4Parse

| Campo | Valor |
|---|---|
| Fecha | 12 de octubre de 2026 |
| CUE4Parse | 1.2.2.202610 (NuGet) |
| Muestra | 20 paks custom de `C:\Games\FUSER-CUSTOM-SONGS-SAMPLE-PAKS` (4 en la raíz, 16 en subcarpetas). Sin DLC parcheados |
| Veredicto | **CUE4Parse sirve**, pero hace falta un lector tolerante para las dos clases de metadatos (ver §2). No se necesita el plan B |

## 1. Riesgos de la sección 7 del documento de arquitectura

| # | Riesgo | Resultado |
|---|---|---|
| 1 | `EGame` correcto | Los paks son de la versión 8 (UE 4.22–4.24). Con `GAME_UE4_22`, el `FText` se lee vacío. Desde `GAME_UE4_23` se lee bien, y de 4.23 a 4.26 el resultado es idéntico. Se usa `GAME_UE4_24` por defecto (`FuserPak.DefaultGame`) y se puede cambiar con `--game` |
| 2 | Montar un pak suelto | Funciona con `StreamedFileProvider` y `RegisterVfs(path)` + `Mount()`. El punto de montaje es `Fuser/Content/` |
| 3 | Cifrado | Ningún pak de la muestra está cifrado. No hace falta clave AES |
| 4 | Properties de Harmonix | `CelSongSet` y `SongCelData` se deserializan como properties genéricas sin mappings. **Pero el lector estándar falla** por los tamaños erróneos (§2) |
| 5 | Metadatos adicionales | Inventario en §4 |
| 6 | DLC parcheados | No verificado: la muestra no tiene ninguno. Se mantiene `PatchedDlcCatalog` |

## 2. Hallazgo principal: tamaños de property falsos en los paks custom

La herramienta de la comunidad que genera los paks copia los tamaños de su plantilla en las cabeceras de las properties y **no los actualiza** al cambiar los datos:

| Property | Tamaño declarado (siempre) | Tamaño real |
|---|---|---|
| `Artist` (TextProperty) | 25 | Depende del texto (18–44 en la muestra) |
| `Title` (TextProperty) | 22 | Depende del texto (17–38) |
| `PickupBeats` (ArrayProperty de float) | 44 | 4 + 4 × N |
| `IsStreamOptimized` (BoolProperty) | 1 | 0 (en UE4 el valor de un bool va en la cabecera) |

CUE4Parse salta a `inicio + tamaño declarado` después de cada property, así que se desalinea en la primera discrepancia y pierde todas las properties siguientes: género, año, key, BPM y modo. La v1 funcionaba porque lee de forma secuencial e ignora el tamaño.

**Solución** (`src/FuserSongManager.Infrastructure/Pak/TolerantTaggedObject.cs`):
- Se registran `CelSongSet` y `SongCelData` en `ObjectTypeRegistry` con un `Deserialize` propio que lee las properties **de forma secuencial**, igual que la v1.
- Para leer cada valor reutiliza el lector interno de CUE4Parse (`FPropertyTagType.ReadPropertyTagType`), al que se accede con `[UnsafeAccessor]`. Así se aprovechan todos los tipos de property de CUE4Parse sin reimplementarlos.
- Si la lectura secuencial de una property lanza una excepción, se usa el tamaño declarado como respaldo y se registra una advertencia.
- **Riesgo:** depende de un método interno de CUE4Parse. Si cambia de firma al actualizar la librería, el error aparece al ejecutar, no al compilar. Hay que cubrirlo con la prueba de paridad (Fase 1) y fijar la versión de CUE4Parse.

## 3. Paridad con la v1

`tools/v1_reference_dump.py` ejecuta `loader.get_song_info` de la v1, y `FuserSongManager.Tools compare` contrasta campo a campo.

**Resultado: 20/20 paks coinciden en los siete campos** (título, artista, año, género, key, modo y BPM).

Rendimiento de referencia (secuencial, un solo hilo, en caliente): **unos 150–200 ms para leer los 20 paks** con la CLI, que abre el pak y carga los dos assets `Meta_*` por canción.

Mapeo de nombres (lo usa `PakSongMetadataExtractor`):

| Campo | Asset | Property | Tipo |
|---|---|---|---|
| Artista | `DLC/Songs/{n}/Meta_{n}` (`CelSongSet`) | `Artist` | TextProperty |
| Género | ídem | `Genre` | EnumProperty `EGenre::*` (`Num` → Indie, `None` → Other) |
| Año | ídem | `Year` | IntProperty |
| Título | `Audio/Songs/{n}/{n}bs/Meta_{n}bs` (`SongCelData`) | `Title` | TextProperty |
| Key | ídem | `Key` | EnumProperty `EKey::*` |
| Modo | ídem | `Mode` | EnumProperty `EKeyMode::*` |
| BPM | ídem | `BPM` | IntProperty |

## 4. Inventario de datos adicionales (riesgo 5)

**Contenido de cada pak.** Unos 100 archivos por canción:
- 4 cels: `bs`, `bt`, `ld` y `lp`, es decir, bajo, beat, lead y loop. Cada cel tiene su `Meta_*`, su `Meta_*_trans`, los MIDI mayor y menor en versión normal y de transición, y los `patches/*_fusion`.
- `DLC/Songs/{n}/Meta_{n}`.
- La portada en `UI/AlbumArt/T_{n}` y `T_{n}_small`. **Está en 18 de los 20 paks**; faltan `TOP_Heat_ShpQn` y `We_Found_Love`.

**Properties que la v1 descarta y que ya se leen:**

| Asset | Property | Posible uso |
|---|---|---|
| `CelSongSet` | `SongIconImage`, `SongIconImage_Large` | Ruta de la portada (RF-PEN-14) |
| `CelSongSet` | `Cels` | Lista de los 4 cels |
| `SongCelData` | `Instrument`, `SubInstrument` | Instrumento de cada cel (p. ej. Synth / BassGuitar) |
| `SongCelData` | `Intensity` | Intensidad (p. ej. Medium) |
| `SongCelData` | `SongLengthTicks` | Duración (en ticks MIDI) |
| `SongCelData` | `PickupBeats` | Beats de entrada |
| `SongCelData` | `Transposes` | Transposición por tonalidad |
| `SongCelData` | `Major/MinorMidiSongAssets` | Referencias MIDI |

**Hipótesis por verificar:**
- **Portada:** que CUE4Parse decodifique las texturas `T_*` a una imagen (`UTexture2D.Decode`). Es el candidato más claro para un dato adicional visible (criterio de éxito 6).
- **Audio (RF-PEN-13):** no hay `USoundWave`, `.wem` ni `.bnk`. El audio probablemente va dentro de los `*_fusion` (formato de Harmonix). Previsualizarlo requiere investigación aparte.
- **Duración en segundos:** podría calcularse a partir de `SongLengthTicks` y `BPM` si se conoce la resolución de ticks.

### 4.1 Portada y audio: verificado (12/10/2026)

Se comprobó con un prototipo que vive fuera del repositorio. Se ejecutó sobre los 20 paks de muestra y sobre la biblioteca real (254 paks, en una copia de solo lectura). La estructura del audio se tomó del código de [FuserCustomSongCreator](https://github.com/NarrikSynthfox/FuserCustomSongCreator) (`src/uasset.h`, `moggcrypt/`).

**Portada (RF-PEN-14): sí está en el pak y se decodifica.**
- Está en `UI/AlbumArt/T_{n}` (1080×1080) y `T_{n}_small` (512×512). Son texturas `UTexture2D` en formato `PF_DXT1`.
- En el prototipo se decodificaron con `CUE4Parse-Conversion`. En la app se usa un decodificador DXT1/DXT5 propio (`DxtDecoder`) y SkiaSharp para el PNG, porque `CUE4Parse-Conversion` trae `SixLabors.ImageSharp` 3.1.12, que tiene vulnerabilidades conocidas (NU1902/NU1903), y las versiones corregidas cambian de licencia.
- La tienen 224 de los 254 paks reales (el 88 %) y 18 de los 20 de muestra. Ninguna falló al decodificar.
- Decodificar la versión pequeña tarda unos 3 ms por canción.

**Audio (RF-PEN-13): sí está en el pak y se puede reproducir.**
- **Dónde está.** Cada cel (`bt`, `bs`, `lp`, `ld`) tiene un `patches/{cel}_fusion.uexp`. Dentro hay un objeto de Harmonix (`HmxAssetFile`) con un paquete de recursos:
  - un `FusionPatchResource`, que es texto con forma de lista;
  - uno o más `MoggSampleResource`.
- **Formato de cada `MoggSampleResource`.** Una cabecera `mogs` (frecuencia de muestreo, canales, número de muestras, tamaño) seguida de un archivo **MOGG**.
- **Qué es cada audio.** El `FusionPatchResource` define las *keyzones*. Cada una tiene una etiqueta (`Major`, `Minor` o `Unpitched`), `sample_path`, `root_note`, el rango de notas, `unpitched` y `orig_tempo`, y así dice qué audio corresponde a cada tonalidad.
- **Cuántos audios hay por cel.** El beat tiene 1 zona. El bajo y el loop suelen tener 2 (mayor y menor). El lead tiene de 1 a 4.
- **Transiciones.** Los `*_trans_fusion` contienen transiciones de unos 9,6 s.
- **Cifrado.** Los MOGG son de la versión `0xB`: AES-128-CTR con la clave pública `ctrKey0B` del proyecto moggcrypt y el IV en los 16 bytes previos al Ogg.
  - El contador avanza 1 por bloque en sus 64 bits bajos (little-endian).
  - Al descifrar queda un Ogg Vorbis normal: 44,1 kHz, estéreo y unos 61 s por zona en las muestras.
  - Todas las páginas Ogg pasan la comprobación de CRC.
- **Cobertura.** Los 1802 MOGG de la biblioteca real son de la versión `0xB`. NVorbis los decodifica todos completos, sin errores.
- **Para escuchar la canción.** Se mezclan las 4 cels con las zonas que el juego usa en el modo de la canción.
  - **Las etiquetas no sirven para elegir.** Son texto libre ("Major Sampler", "Rap", "Jay-Z"…) y algunos paks antiguos no las tienen.
  - **Lo que cuenta es el rango de notas MIDI.** Las zonas de 0 a 71 son la versión mayor y las de 72 a 127 la menor.
  - **Varias zonas pueden sonar a la vez.** Se toman todas las que contienen la nota del modo (60 en mayor, 84 en menor), porque el preset las reproduce en capas (`layer_select_mode "layers"`). Por ejemplo, un sampler más un sintetizador, o una voz *unpitched* de 0 a 127.
  - **Paks que no siguen la convención** (1 de 254: un kit de batería con un sample por nota): se usa la primera zona.
- **Pistas vacías.** El creador las permite: *TheyDontCareAboutUsMJ* no tiene bajo. Su Ogg es válido pero tiene 0 muestras, así que se descarta.
- **Búsqueda dentro del audio.** NVorbis rechaza el *granule* de la última página de estos Ogg al buscar una posición ("GranulePos mismatch"): fallaron 13 de 680 búsquedas, todas en el último 3 %. Sin embargo, la página se decodifica bien leyendo de forma secuencial. Por eso `VorbisStemSource` busca un poco antes y decodifica hasta el punto exacto; tarda unos 17 ms.
- **Coste.** Hay que leer unos 4–8 MB del pak por canción. Decodificar todas las zonas de una canción de una vez tarda unos 1,3 s. Para reproducir no hace falta: el audio se decodifica mientras suena.
- **No verificado:**
  - Los DLC parcheados (oficiales de Harmonix): podrían usar otra versión de MOGG que no se pueda descifrar.
  - El volumen relativo de cada cel: los presets del fusion indican, por ejemplo, −3 dB.

**Paquetes usados en la app.** `NVorbis` (MIT) para decodificar el Ogg, `NAudio.WinMM` 2.2.1 (MIT; la misma versión de `NAudio.Core` que ya trae CUE4Parse) para la salida de audio, y `SkiaSharp` (MIT; la versión que usa Avalonia) para codificar la portada en PNG.

**Implementado** el 12/10/2026: ver el bloque "Después del MVP" en `PLAN_MVP.md`.

## 5. Uso de la CLI

```
FuserSongManager.Tools list    <pak>
FuserSongManager.Tools dump    <pak> [--out file.json]
FuserSongManager.Tools read    <pak|dir> [--json]
FuserSongManager.Tools compare <v1.json> <dir>
  --game <EGame>   (por defecto GAME_UE4_24)
  --verbose        (traza de CUE4Parse y de las properties con tamaño falso)
```

Para regenerar la referencia de la v1 se necesita un entorno de Python con `PyPAKParser==1.2.0`:

```
python tools/v1_reference_dump.py ../FuserCustomSongManager/src <paks> v1.json
```

## 6. Pendientes

- Conseguir 1 o 2 paks de DLC parcheados para cerrar el riesgo 6.
- ~~Probar la decodificación de la portada.~~ Hecho; ver la sección 4.1.
- Medir el escaneo en paralelo en la Fase 1.
