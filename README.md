<div align="center">
  <img src="app.ico" width="64" height="64" alt="QuickNotes Logo"/>
  <h1>QuickNotes</h1>
  <p>Aplicación de notas tipo sticky notes para Windows</p>
  <p>
    <strong>.NET 9 · WPF · SQLite</strong>
  </p>
</div>

---

## 📝 Descripción

QuickNotes es una app de notas rápida y liviana para Windows. Crea notas con color, organízalas arrastrándolas, fíjalas al inicio, minimízalas como tabs, y edita con formato rich text. Todo se guarda localmente en SQLite.

---

## ✨ Funcionalidades

- **Notas con color** — paleta de 16 colores (8 claros + 8 oscuros)
- **Pin** — fija notas importantes arriba de la lista
- **Minimizar** — oculta la nota como tab en la barra lateral
- **Multi-ventana** — cada nota se abre en su propia ventana flotante
- **Rich text** — edición con formato (FlowDocument), checkboxes inline tocables
- **Búsqueda** — filtra notas por título y contenido
- **Temas** — oscuro, claro y sistema
- **Auto-guardado** — configurable cada 5, 10, 30 o 60 segundos
- **Backup automático** — copia de seguridad diaria de la base de datos
- **Modo compacto** — reduce márgenes y fuente para más contenido
- **Restauración** — al reabrir recupera todas las ventanas y tabs donde estaban
- **Animaciones** — transiciones suaves (desactivables)

---

## 🛠️ Stack

| Capa | Tecnología |
|---|---|
| **Lenguaje** | C# 13 |
| **Framework** | .NET 9.0, WPF |
| **Base de datos** | SQLite (Microsoft.Data.Sqlite 9.0) |
| **Persistencia** | Local (`%USERPROFILE%\Documents\QuickNotes\notes.db`) |
| **Formato de notas** | FlowDocument serializado como XAML |
| **Icono** | `app.ico` (multi-resolución 16–256 px) |

---

## 🚀 Cómo compilar

### Requisitos

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Windows (WPF)

### Compilar y ejecutar

```bash
git clone https://github.com/ulquiorra1405/QuickNotes-WPF.git
cd QuickNotes-WPF/QuickNotes
dotnet run -c Release
```

O publicar como ejecutable independiente:

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

---

## 📁 Estructura del proyecto

```
QuickNotes/
├── App.xaml / App.xaml.cs          # Entry point + manejador global de errores
├── MainWindow.xaml / .cs           # Ventana principal (lista de notas, drag & drop)
├── NoteWindow.xaml / .cs           # Editor flotante de notas (rich text)
├── TabBar.xaml / .cs               # Barra lateral de tabs (notas minimizadas)
├── Views/
│   ├── NoteCard.xaml / .cs         # Tarjeta de nota reutilizable
│   └── SettingsWindow.xaml / .cs   # Ventana de configuración
├── Models/
│   ├── Note.cs                     # Modelo de nota + paleta de colores
│   ├── NotesStore.cs               # Capa de datos (SQLite, backup, migración legacy)
│   ├── ErrorLog.cs                 # Log de errores a archivo
│   └── AnimationHelper.cs          # Helper de animaciones
├── Resources/
│   └── Styles.xaml                 # Estilos reutilizables (ComboBox, etc.)
├── app.ico                         # Icono de la aplicación
└── QuickNotes.csproj               # Proyecto .NET 9
```

---

## 📦 Estado del desarrollo

- ✅ **Fase 0** — Bugs críticos corregidos
- ✅ **Fase 1** — Refactor estructural (NoteCard, SettingsWindow, estilos)
- ✅ **Fase 2** — Robustez (WAL mode, saves críticos, limpieza de IDs)
- ⏳ **Fase 3** — Features (pendiente de definir)

---

## 📄 Licencia

Uso personal.
