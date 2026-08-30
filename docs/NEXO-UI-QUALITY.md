# NEXO UI Quality Module

## Objetivo

La UI de NEXO parte de una base funcional sólida, pero el objetivo del módulo es convertirla en un sistema visual coherente, mantenible y suficientemente flexible para crecer sin acumular estilos locales inconsistentes.

La valoración inicial de referencia para esta línea de trabajo es **7.5/10**. La meta no es perseguir una cifra artificial, sino eliminar los factores que actualmente impiden que la interfaz se sienta como un producto terminado.

## Problemas detectados

La primera auditoría de `MainWindow.xaml` encontró:

- colores y tamaños definidos directamente en múltiples controles;
- estilos principales encapsulados sólo dentro de la ventana;
- iconografía basada en caracteres Unicode con métricas diferentes;
- distintos lenguajes visuales entre botones, campos y combos;
- jerarquías visuales repetidas manualmente;
- adaptación limitada a cambios de tamaño;
- estados de foco menos cuidados que hover/pressed;
- un `MainWindow` demasiado responsable tanto de UI como de lógica.

## Fase 1: Design system

Implementada en `src/NexoLauncher.App/UI/NexoTheme.xaml`.

Incluye tokens semánticos para:

- background;
- sidebar;
- surfaces;
- border;
- texto principal/secundario/muted;
- accent;
- success;
- warning;
- danger.

También incluye estilos para:

- labels;
- text fields;
- primary buttons;
- secondary buttons;
- ghost buttons;
- navigation buttons;
- combo boxes;
- cards;
- tooltips;
- checkboxes;
- progress bars.

La intención es que las pantallas nuevas consuman estos estilos directamente y que las existentes migren gradualmente.

## Fase 2: Compatibility layer

`NexoUiQualityModule` permite aplicar el design system nuevo a controles que todavía usan claves legacy (`PrimaryButton`, `FieldStyle`, etc.) sin reescribir de golpe toda la ventana principal.

También:

- aplica colores semánticos al shell;
- sincroniza el número de versión mostrado con el assembly;
- mejora el rendering de texto;
- incorpora un primer responsive pass para ventanas compactas.

Esta capa es temporal. El objetivo final es que los estilos legacy desaparezcan del XAML principal.

## Fase 3: Library Experience

Pendiente.

Objetivos:

- tarjetas de instancia con estados más informativos;
- loaders representados de manera consistente;
- acciones frecuentes más visibles;
- duplicar perfil como acción de primer nivel, no sólo menú contextual;
- mejores empty states;
- estado de ejecución integrado en la tarjeta correspondiente;
- estados de error/repair sin depender exclusivamente de MessageBox.

## Fase 4: Creation Flow

Pendiente.

La creación de instancia debe convertirse en un flujo guiado que reduzca decisiones innecesarias:

1. versión;
2. loader;
3. configuración recomendada;
4. resumen;
5. instalación.

Java debe permanecer automático por defecto. La RAM debe mostrar claramente recomendado, seleccionado y límite seguro.

## Fase 5: Content Hub

Pendiente.

Objetivos:

- búsqueda y filtros más claros;
- distinguir Modrinth, archivos locales e imports;
- progreso por operación;
- compatibilidad visible antes de instalar;
- estado transaccional de importación;
- errores recuperables dentro de la pantalla;
- futuro acceso a optimización opcional por instancia.

La lógica de optimización del proceso Minecraft ya se documenta por separado en `docs/NEXO-PERFORMANCE.md`; la UI futura deberá exponer únicamente controles comprensibles, no flags JVM crudos.

## Fase 6: Settings / Accounts

Pendiente.

Objetivos:

- separar sistema, Java, memoria, comportamiento y cuentas;
- añadir autenticación Microsoft cuando la implementación esté lista;
- indicar qué valores son globales y cuáles son overrides de instancia;
- evitar ajustes que el usuario no necesita entender para jugar.

## Accesibilidad y calidad

Cada componente debe considerar:

- keyboard focus visible;
- contraste;
- lectura a 100–150% de escala;
- tooltips donde una acción sólo tenga icono;
- orden de tabulación razonable;
- disabled state identificable sin depender sólo del color;
- texto completo disponible mediante tooltip cuando se trunca.

## Responsive

El shell inicial tiene dos modos:

- normal: sidebar 252 px y detalle 350 px;
- compacto: sidebar 228 px y detalle 315 px.

Esta adaptación todavía no convierte NEXO en una UI completamente responsive. La siguiente fase debe sustituir anchos rígidos restantes por breakpoints y layouts que puedan reordenarse.

## Criterio de salida

La mejora UI no debe considerarse finalizada sólo porque el tema sea más atractivo. Antes de declararla estable deben probarse manualmente:

- Biblioteca vacía y con varias instancias;
- nombres largos y nombres repetidos;
- Vanilla/Fabric/Forge/NeoForge;
- ventana mínima y maximizada;
- escalado de Windows;
- Crear instancia;
- Contenido;
- Configuración;
- diálogos de Java y edición;
- estados busy/error;
- instancia en ejecución.

La nota visual debe subir como consecuencia de resolver estos problemas, no como objetivo aislado.
