# QuickNotes — Arquitectura

## Visión general

QuickNotes es una app de notas tipo sticky notes para Windows. Cada nota vive en su propia ventana flotante con formato enriquecido, persistencia SQLite local, dock lateral y temas claro/oscuro.

**Stack:** WPF .NET 9, SQLite (WAL mode), `System.Data.SQLite`, `WindowChrome` para titlebar nativo.

**Ubicación:** `C:\Users\fbatista\source\repos\quicknotes\QuickNotes` (el `QuickNotes/` anidado es el repo, no la carpeta contenedora).

---

## 1. Arquitectura general — cómo se relacionan los componentes

```
MainWindow ←→ NotesStore (SQLite)
    │
    ├── NoteCard (UserControl, una por nota en la lista)
    ├── NoteWindow (uno por nota abierta, ventana independiente)
    ├── DockWindow (lateral derecho, singleton)
    └── SettingsWindow (diálogo modal)
```

### MainWindow
El centro de todo. Tiene tres áreas en un Grid:
- **Title bar** (fila 0, 30px): logo QN, botones menú/nueva nota, búsqueda
- **Cuerpo** (fila 1): sidebar + lista de tarjetas (NoteCard)
- **Status bar** (fila 2): texto de estado + botón "Clear"

Mantiene:
- `NotesStore store` — la capa de datos (singleton compartido)
- `ListCollectionView _view` — vista filtrada/ordenada de `store.Notes`
- `DockWindow? _dockWindow` — referencia al dock (singleton)
- `string _currentTheme` — `"dark"` | `"light"`
- Un `DispatcherTimer` para auto-guardado diferido

### NoteWindow
Una ventana WPF independiente por nota abierta. No hay límite. Cada una recibe su `Note` y el `NotesStore` por constructor. Se posiciona autónomamente y guarda su posición al cerrar.

### NotesStore
Capa de datos. Carga/guarda todo: notas, tags, libretas, settings. Expone `ObservableCollection<Note>` para binding directo con la UI.

### NoteCard
UserControl que representa una nota en la lista. Se bindingea a `Note`. Incluye: título editable inline, color picker, pin toggle, context menu, indicador de adjuntos.

---

## 2. Persistencia — SQLite con WAL

### Base de datos
Archivo único en `%USERPROFILE%\Documents\QuickNotes\notes.db`. Ruta fija (pendiente migrar a `LocalAppData`).

### Tablas principales
```sql
notes (Id TEXT PK, Title, Text, Color, Icon, IsMinimized, IsPinned,
       OrderNum, LastModified, WinLeft/Top/Width/Height,
       IsArchived, IsDeleted, DeletedAt, NotebookId)

tags (Id TEXT PK, Name)
note_tags (NoteId, TagId) -- muchos a muchos

notebooks (Id TEXT PK, Name, Color, Icon)

settings (Key TEXT PK, Value) -- settings de la app
```

### Migraciones automáticas
En `NotesStore.InitializeDatabase()`: cada nueva columna se agrega con `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`. Nunca se borran columnas — se marcan como obsoletas y se migran en versión futura.

### WAL mode
Se activa al abrir cada conexión (`PRAGMA journal_mode=WAL`). Mejora rendimiento de lectura/escritura concurrente.

### Backup automático
Usa `VACUUM INTO` (SQLite nativo) para hacer backup sin bloqueo. Diario, mantiene últimos 10, en `Documents/QuickNotes/backups/`.

### Auto-guardado
Un `DispatcherTimer` en MainWindow (default 10s, clamp 3-300). Al modificarse una nota se marca `IsDirty=true` y se reinicia el timer. Cuando el timer vence, guarda solo las notas dirty.

---

## 3. Ventanas y ciclo de vida

### Abrir nota
`NoteClick` en MainWindow → verifica que no haya ya una NoteWindow abierta para esa nota → crea `new NoteWindow(note, store)` → la registra en `_noteWindows[note.Id]` → llama `Show()`.

### NoteWindow
Se construye con `WindowChrome` y `CaptionHeight=30`. Al cargar:
1. `LoadRichText()` — convierte el XAML guardado en el RichTextBox
2. Restaura posición/tamaño desde la nota
3. `ClampToScreen()` asegura que sea visible
4. Fade in (animation 200ms)
5. `Activated`/`Deactivated` — muestra/oculta toolbar flotante

### Cerrar nota
`Close_Click` → guarda posición → remueve del diccionario → `Close()`.
Si es la última ventana, no cierra la app (MainWindow sigue abierta).

### Restaurar al iniciar
Al abrir la app, lee `store.OpenNoteIds` (string de GUIDs separados por coma) y reabre las que estaban abiertas. Las posiciones se restauran desde las columnas `WinLeft/Top/Width/Height` de cada nota.

---

## 4. WindowChrome — por qué se usa

En lugar de implementar drag personalizado con P/Invoke, la app usa `WindowChrome` con `CaptionHeight=30`. Esto delega todo el manejo de ventanas a Windows:

- Drag & resize nativo
- DPI scaling automático
- Multi-monitor (incluyendo conectar/desconectar)
- Win+Arrow snap, Win+Shift+Arrow
- Comportamiento de ventana estándar

**Decisión:** Originalmente tenía drag custom + P/Invoke (`GetCursorPos`, `GetDpiScale`, `SendMessage`). Se eliminó por completo el 25-jun-2026 porque WindowChrome lo hace mejor, más estable y con menos código.

---

## 5. Sistema de temas

El theme se maneja desde `MainWindow._currentTheme` y se propaga a todos los componentes.

### ApplyTheme(string theme) — MainWindow
Recibe `"dark"`, `"light"` o `"system"`. Si es `"system"`, lee el registro de Windows (`HKCU\...\AppsUseLightTheme`).

Define paletas de colores para cada sección:
- `bg`, `titleBg`, `statusBg` — fondos
- `textColor`, `textMuted` — texto
- `popupBg`, `popupBorder`, `popupText` — menús popup
- `sidebarSepFg` — separadores
- `_topbarHoverBg`, `_sidebarHoverBg` — hovers
- `_exitBtnHoverBg`, `_dockBgNormal/Hover` — dock

Luego asigna cada color a los elementos XAML correspondientes.

### Cómo se propaga
- **DockWindow:** llama `dock.ApplyTheme(theme)` desde MainWindow.ApplyTheme
- **SettingsWindow:** recibe `_currentTheme` por constructor y define su paleta en `BuildUI()`
- **Diálogos inline** (About, exit confirm, notebook/tag): usan `_currentTheme` directamente con ternarias
- **Sidebar flyout:** mismo patrón, ternaria con `isLight`

### Regla obligatoria
Cada elemento nuevo de UI debe tener equivalente en modo claro. Ver la skill del proyecto para la paleta exacta y el checklist.

---

## 6. Dock lateral

### Qué es
Ventana flotante de 60px de ancho en el borde derecho del monitor. Estilo dock de macOS para acceso rápido a notas sin la MainWindow.

### Cómo se activa
Botón minimizar (⊟) en MainWindow → oculta MainWindow → crea/muestra DockWindow.

### Cómo funciona
- `WindowStyle=None`, `Topmost=True`, `ShowInTaskbar=False`
- Fondo transparente; al hacer hover se vuelve opaco (`#1A1A1A`)
- Cada nota es un icono cuadrado 32x32 con color de fondo + 2 letras/emoji
- Indicadores: barra verde (abierta), punto naranja (minimizada), opaco 0.5 (cerrada)
- ScrollViewer para más de 6 notas
- Botón `>` al fondo cierra el dock y restaura MainWindow

### Drag reorder
Implementación v2 con reorden en vivo: los items se reubican instantáneamente al mover el cursor sobre otro slot. El item arrastrado se atenúa (opacidad 0.25). Usa `GetSlotFromVisualPosition()` contra posiciones reales de containers (no fórmula Y/38).

### Tema claro
El dock siempre usa fondo oscuro (no cambia con el tema), pero el botón de salida y el border se ajustan en modo claro.

---

## 7. Editor (NoteWindow RichTextBox)

### Formato básico
Negrita, itálica, subrayado, tachado — se aplican vía `RichTextBox.Selection.ApplyPropertyValue()` con `FontWeight`, `FontStyle`, `TextDecorations`. Toolbar flotante aparece al seleccionar texto o al hacer hover en la parte superior.

### Encabezados (Fase 3)
Ctrl+1/2/3 o picker flotante. Se aplican cambiando el `FontSize` de la selección actual. No usa estilos de documento, solo tamaño de fuente.

### Checkboxes
Sintaxis inline: `☐` y `☑`. Un click los togglea. Enter al final de una línea con checkbox crea otro checkbox en la línea siguiente. Dos Enter seguidos salen del modo checklist.

### Búsqueda inline (Ctrl+F)
Searchbar que aparece en el titlebar. Resalta todas las ocurrencias con un `TextRange` de fondo amarillo. Navegación ↑↓/Enter entre resultados. Scroll automático al resultado activo.

### Enlaces clickeables
Detección automática de URLs. Ctrl+Click abre el navegador. Se aplican formato al cargar el documento.

### Adjuntar archivos (Fase 3)
Drag-drop de archivos al NoteWindow. Se copian a una carpeta `attachments/` junto a la DB. Ruta relativa en la nota. Lista blanca de tipos, max 10MB por archivo. Se muestran en una lista debajo del editor.

---

## 8. Fases del proyecto

### Fase 1 ✅ — Bug fixes & quick wins
Checkboxes continuos, sangría Tab, enlaces clickeables, B/I/U/S sin quitar foco, marcador "sin color", context menu en tarjetas, resaltado de búsqueda, dock draggable + 9 notas.

### Fase 2 ✅ — Organización
Archivar, papelera, sidebar expandible, tags (muchos a muchos), libretas (1:N), timeline/diario automático.

### Fase 3 ✅ — Editor avanzado
Encabezados (Ctrl+1/2/3 + picker), búsqueda inline con scroll fix, adjuntar archivos.
**Descartado:** tablas, secciones colapsables.
**Pospuesto:** codeblocks (para después de exportación).

### Tema claro ✅ (jul-2026)
Modo claro completo: sidebar, topbar, popups, dock, settings, about, exit confirm, diálogos notebook/tag, flyouts.

### Fase 4 ⏳ — Potencia & conexiones
Exportar (MD/TXT/HTML), importar, backup a nube, plantillas, atajos globales (Win+Shift+Q), indicador de adjuntos.
**Descartado:** links `[[nota]]` (complejidad no justificada).

### Fase 5 📋 — Experiencia & visual
Modo Zen, vista Kanban, recordatorios con toast, estadísticas de escritura, temas personalizables.

---

## 9. Decisiones técnicas clave

| Decisión | Por qué |
|----------|---------|
| WindowChrome en vez de drag custom | Windows lo hace mejor: DPI, multi-monitor, snap, sin P/Invoke |
| SQLite en Documents, no AppData | Histórico; pendiente migrar |
| WAL mode | Mejor concurrencia lectura/escritura |
| VACUUM INTO para backup | No bloquea la DB, SQLite nativo |
| ObservableCollection + ListCollectionView | Binding reactivo sin recargar toda la lista |
| NoteWindow independiente por nota | Máxima flexibilidad, cada nota es su propia ventana |
| RichTextBox con XAML persistente | Formato enriquecido nativo de WPF, serializable |
| Temas vía code-behind + ternarias | Más simple que ResourceDictionaries dinámicos; fácil de leer y mantener |
| ShowConfirm de static a instance | Necesitaba acceder a `_currentTheme` |

---

*Documento generado el 1-jul-2026. Basado en el código fuente de QuickNotes en `ulquiorra1405/QuickNotes-WPF`.*
