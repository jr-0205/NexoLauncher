# NEXO UI Quality Module

## Objetivo

Elevar la interfaz de NEXO desde una base funcional aproximada de **7.5/10** a una experiencia de producto consistente, rápida y mantenible de **9/10 o superior**, sin introducir Electron, WebView ni dependencias visuales innecesarias.

La UI sigue siendo WPF nativa. El módulo no modifica la lógica de Minecraft, loaders, Java, perfiles o almacenamiento.

## Principios

1. **Jerarquía antes que decoración.** La acción principal de cada pantalla debe entenderse en menos de un segundo.
2. **Consistencia antes que variedad.** Un mismo tipo de acción usa el mismo componente, espaciado y estado visual.
3. **Estado siempre visible.** Cargando, listo, error, deshabilitado, ejecutando y selección deben distinguirse sin depender únicamente del texto.
4. **Teclado y foco son ciudadanos de primera clase.** Los controles interactivos deben mostrar foco y conservar navegación coherente.
5. **Responsive dentro del escritorio.** NEXO debe conservar jerarquía desde su ancho mínimo hasta pantallas grandes sin cortar acciones críticas.
6. **Cero lógica de negocio en el tema.** El módulo visual puede presentar estado; no decide instalación, autenticación, filesystem o lanzamiento.
7. **Tokens semánticos.** Las vistas no deben inventar nuevos hexadecimales cuando existe un token de superficie, borde, texto o estado.

## Arquitectura

```text
NexoLauncher.App/
└── UI/
    ├── NexoTheme.xaml
    └── NexoUiQualityModule.cs
```

`NexoTheme.xaml` es la fuente central de:

- paleta semántica;
- superficies y bordes;
- texto principal/secundario/muted;
- accent, success, warning y danger;
- foco de teclado;
- botones primarios, secundarios y ghost;
- campos de texto;
- ComboBox;
- navegación;
- cards;
- ToolTip, CheckBox y ProgressBar.

`NexoUiQualityModule` permite migrar incrementalmente el XAML histórico. Durante `Loaded` identifica los estilos legacy de `MainWindow` y los sustituye por componentes del design system. Esto evita una reescritura masiva de una pantalla funcional y permite retirar los estilos locales gradualmente.

También:

- sincroniza el texto de versión visible con la versión real del assembly;
- activa ClearType y text formatting apropiado para UI;
- aplica colores base del shell;
- ajusta densidad del sidebar y panel de detalles en anchos compactos.

## Scorecard de calidad

La evaluación futura de la UI debe dividirse en criterios concretos, no en una nota subjetiva única:

| Área | Peso | Objetivo |
|---|---:|---:|
| Jerarquía visual y legibilidad | 20% | 9/10 |
| Consistencia de componentes | 20% | 9.5/10 |
| Estados y feedback de operaciones | 15% | 9/10 |
| Navegación, teclado y foco | 15% | 9/10 |
| Responsive / escalado de escritorio | 10% | 9/10 |
| Accesibilidad y contraste | 10% | 9/10 |
| Pulido visual y microinteracciones | 5% | 8.5/10 |
| Coste de mantenimiento | 5% | 9.5/10 |

## Fase actual · UI Foundation

Implementado:

- design tokens centralizados;
- estilos reutilizables;
- estados hover / pressed / disabled;
- foco visible;
- remapeo de estilos legacy;
- versión visible automática;
- shell dark coherente;
- primer breakpoint compacto para biblioteca/sidebar;
- ToolTips, CheckBox y progreso alineados con el tema.

## Próximas fases

### UI 2 · Library Experience

- tarjetas de instancia con icono/loader/versión/status reales;
- badges semánticos en lugar de texto decorativo fijo;
- búsqueda y ordenamiento de instancias;
- acciones secundarias en menú contextual consistente;
- mejores empty/loading/error states;
- panel de detalles con agrupación y menor ruido visual.

### UI 3 · Install Flow

Convertir “Nueva instalación” en un flujo más guiado:

1. Minecraft;
2. loader;
3. nombre del perfil;
4. runtime/memoria avanzada;
5. resumen;
6. instalar/crear.

La configuración avanzada debe existir sin competir visualmente con el camino normal.

### UI 4 · Content Hub

- separar claramente catálogo y archivos locales;
- distinguir Modrinth y CurseForge como proveedores;
- chips de compatibilidad Minecraft/loader;
- progreso por dependencia/archivo;
- resultado final con resumen de cambios;
- estados de error accionables y no sólo MessageBox.

### UI 5 · Settings and Accounts

- navegación interna por secciones cuando crezca configuración;
- cuenta Microsoft como identidad visible, no como campo suelto;
- runtimes Java como inventario inspeccionable;
- configuración global frente a override de instancia claramente diferenciada.

## Reglas para nuevos controles

- no introducir colores hexadecimales nuevos en vistas sin justificar un token;
- no usar emoji como iconografía funcional permanente;
- no crear otro template de botón si el caso cabe en Primary, Secondary, Ghost o Danger;
- todo control interactivo debe tener estado disabled y foco visible;
- operaciones de más de un instante deben mostrar estado/progreso;
- errores deben indicar qué pasó y qué acción puede tomar el usuario;
- el nombre visible de una instancia nunca debe confundirse con su ruta física/GUID.

## Definición de terminado para una pantalla

Una pantalla sólo se considera terminada cuando:

- funciona con ratón y teclado;
- mantiene contraste suficiente;
- no pierde controles esenciales al redimensionar hasta el mínimo soportado;
- loading, empty, success y error están definidos cuando apliquen;
- usa componentes del design system;
- no duplica estilos locales sin una razón documentada;
- no bloquea el hilo UI con trabajo de red/disco;
- no introduce regresiones en la lógica funcional existente.
