---
name: pokemanager-design-system
description: Use whenever a view, control, style or visual detail of Pokemanager (Avalonia, .axaml) is created, changed or reviewed, to apply its design system: colours, type colours, spacing, text sizes, icons and interaction rules.
---

# Pokemanager design system

## 1. Context

Pokemanager is a **Windows desktop app** (Avalonia 12) used by one player while they play a 3DS Pokémon game on Citra:
randomize and build the ROM, edit the save, follow a Nuzlocke. The window sits next to the emulator, so it must be
**readable at a glance and dense**, like PKHeX / UPR ZX, not like a web page. Mouse and keyboard, no touch.

Look chosen with the user on 2026-11-11 ("Grafito + color de tu juego"): **dark slate by default, light in Ajustes**,
square edges (2 px radius), the **accent is the colour of the game being edited**, sentence case everywhere (never
all-caps).

Source of truth for the look, in this order:

- `src/Pokemanager.App/App.axaml`: the colour **tokens** (`ThemeDictionaries` Dark/Light), `Border.card`,
  `TextBlock.card-title/.section/.hint/.error/.dot`, `field-label.modified`.
- `src/Pokemanager.App/Styles/ClassicTheme.axaml`: controls on the tokens (buttons, inputs, tabs, lists, menus).
- `src/Pokemanager.App/Services/AppTheme.cs`: variant, background and the accent brushes that follow the game.
- `src/Pokemanager.App/Services/Tokens.cs`: the tokens for C# that builds brushes (read them when drawing, never into a
  static field: the variant changes at run time).
- `src/Pokemanager.App/Styles/PokemonLook.axaml`: **everything that shows a Pokémon** (party cards, box cells, detail
  header, badges, stat bars, move rows, evolution cards, `MonSheet`). Reuse these classes and keyed templates; **never
  copy their styles into a view** (local styles win and the looks drift apart).
- `src/Pokemanager.App/Services/TypeColors.cs`: the 18 type colours and the text colour on them.

## 2. Colour

**Always `{DynamicResource XBrush}`, never a hex value in a view.** A new colour needs a token in both variants first.

| Token (`…Brush`) | Dark | Light | Use |
|---|---|---|---|
| `AppBase` / `AppBase2` | `#22262E` / `#2A2F38` | `#EEF0F3` / `#F7F8FA` | window gradient (also `…Color`) |
| `Panel` / `Panel2` | `#2B3039` / `#333944` | `#FFFFFF` / `#F1F3F6` | cards, flyouts / buttons, empty slots |
| `Field` | `#1E2228` | `#FFFFFF` | inputs |
| `Bar` | `#1D2026` | `#FFFFFF` | top bar, status bar |
| `Line` / `InputLine` | `#3C4350` / `#5A6372` | `#D5DAE1` / `#8A929E` | rules, card borders / input borders |
| `Text` / `Muted` | `#ECEEF2` / `#A9B1BD` | `#1B1E24` / `#5B6370` | body / hints (≥ 4.5:1 on Panel) |
| `Hover` / `Pressed` | `#3A414D` / `#454D5B` | `#E6EBF1` / `#D8DFE8` | control states |
| `Disabled` / `DisabledText` | `#262A31` / `#6F7784` | `#EEF0F3` / `#A0A6AF` | disabled only |
| `Modified` / `ModifiedText` | `#F5A524` / `#F5B54A` | `#E07B00` / `#7A4B00` | shapes / text of "modified" |
| `Error` / `ErrorSoft` | `#FF6B6E` | `#C42B2F` | problems / their fill |
| `Success` | `#4CC38A` | `#237A4B` | `hint.current`, earned ✓ |
| `Warning` / `WarningLine` | amber 20 % / `#B57A16` | amber 12 % / `#E07B00` | the "not in a ROM yet" banner |
| `Overlay` | `#33000000` | `#14000000` | dimming |

Set by `AppTheme` (follow the game): `Accent` (filled: primary, selection, focus, checks), `AccentSoft` (tonal fill,
selected side tab, badges), `AccentText` (card titles, accent text: 35 % towards white/black, keeps 4.5:1), `AccentInk`
(text on a filled accent: white or `#16130F`, whichever contrasts more), `AppGlow`, `AppPattern`, `BarGlow`.
Game colours: X `#2F7FE0`, Y `#E0414B`, OR `#D9483B`, AS `#2F77D0`, Sun `#F29A2E`, Moon `#7B6CF0`, US `#F28C28`,
UM `#9B6CF2`, none `#3B82F6`.

Backgrounds (Ajustes → Apariencia): Brillo del juego (default), Hexágonos, Poké Balls, Líneas, Liso.

Kept fixed on purpose: type colours and the text on them, badges (`badge-value`/`badge-label` on a light chip), gold
`#FFD700`, the save-dex flag colours, the roulette palette. Nature marks: `Tokens.Raised` / `Tokens.Lowered`.

**Type colours** (`TypeColors`, game order: Normal, Fighting, Flying, Poison, Ground, Rock, Bug, Ghost, Steel, Fire,
Water, Grass, Electric, Psychic, Ice, Dragon, Dark, Fairy) are the Showdown palette. Two types = **hard diagonal cut**
(both gradient stops at 0.5). Text on them always comes from `TypeColors.Foreground` (white below luminance 0.62,
`#1A1A1A` above); never hard-code a text colour on a type background. Stat bars use the Showdown stat colours of
`StatFieldViewModel` / `StatBars`; nature marks: raised ▲ red, lowered ▼ blue.

### Buttons (one primary per screen)

| Class | Size | Use |
|---|---|---|
| `primary` | 38 px, 14 SemiBold, accent fill | the one main action (Crear ROM while there are unbuilt changes, else Jugar; Abrir proyecto) |
| `tonal` | 38 px, accent frame on soft fill | the second important action beside it (Jugar while the ROM is not built) |
| `accent` | 30 px, accent fill | the main action of a card or dialog (Escribir en la partida, Crear sala) |
| (none) | 30 px | ordinary secondary actions (Guardar) |
| `text` | 26 px, underlined, muted | optional / look-up (Ver log) |
| `icon` (+`ghost`) | 30×30 | glyph with a tooltip (↶ ↷ ⋯) |
| `danger` | 26 px, error frame | what cannot be undone, kept apart |

Rare destinations go in the ⋯ menu (Historial, Ajustes), not as buttons; ◀ at the left of the editor bar goes back to
the projects.

### Blocks, folding, windows (2026-11-11)

- Cards of a page go in a `c:CardGrid` (never a `WrapPanel` of fixed-width cards): equal columns, a row as tall as its
  tallest card, the tallest cards into a short last row at full width. Cards have no `Width`, `Margin` or
  `VerticalAlignment`; mark the one that must stay first with `c:CardGrid.Fixed`. One card alone loses its frame.
  Advanced editors use `Classes="dense"` and must fit a 1600×1000 window without scrolling.
- Options inside a card that can become wide flow in a `ColumnFlowPanel`, so a full-width card spreads them across.
- A side panel or a top strip that can be folded uses `FoldState` + `Button.fold` with a `c:Chevron` (subtle, muted).
- Every tab can be torn off (`TabTearOff`): do not rely on a view's `UserControl.Resources` with `DynamicResource`
  inside a tab's content; styles are copied, resources are not.
- An edited field sits in a `Border.field` with `Classes.modified`: a 2 px accent bar and the label in `AccentText`.

## 3. Spacing

4 px scale, with the classic compact exceptions already in use:

| Value | Where |
|---|---|
| 1–2 | ruled rows (`info-row`), chip padding, between a label and its value |
| 4 | inside a group of controls, `Spacing="4"` in toolbars |
| 6 | default stack spacing inside a card (`Spacing="6"`, the most used) |
| 8 | between cards, card bottom padding, list item padding |
| 10–12 | card side padding (`10,6,10,8`), space between sections |
| 16–24 | page margins, gaps between columns of `AdaptiveColumnsPanel` |

New code uses 2 / 4 / 6 / 8 / 12 / 16 / 24; avoid 3, 5 and odd one-offs.

## 4. Typography

Segoe UI (Tahoma, Verdana fallback), **13 px base** everywhere (`Window`, controls). Sentence case only. Scale:

| Role | Size / weight |
|---|---|
| display (detail header name, level) | 18–22 SemiBold |
| h1 (page or dialog title) | 16 SemiBold |
| h2 (`card-title`, `section`) | 13 Bold, `AccentText` |
| body | 13 Normal |
| small (chips, badges, evolution "how", timeline labels) | 11 SemiBold |
| label (type chip text only) | 10 SemiBold, on a type colour |

At most three weights (Normal, SemiBold, Bold). Numbers that line up (stats, levels, money, IVs/EVs) are right-aligned
in fixed-width columns. Nothing new below 11 px; the 10 px type chip is the single exception.

## 5. Icons and images

There is no icon font: the app uses **the games' own art** and a few text glyphs.

| Concept | Image / glyph |
|---|---|
| Pokémon | box icon from the ROM (`PokemonSprites`, always centred, `RenderOptions.BitmapInterpolationMode="None"`) |
| type | PKHeX `type_icon_NN` or a type chip |
| move category | `PkhexImages.Category` (red burst / blue rings / grey half circle) |
| item, ball | PKHeX item and ball sprites; Z-crystals via `PkhexImages.Piece` / `CrystalIcon` |
| badge / trial | the game's badge or crystal art (`BadgeIcons`, `ItemIcons.LoadTypeCrystals`) |
| play / back / next | `▶` `◀` `▶` text |
| close / remove | `✕` |
| life | `♥` |
| earned / done | `✓` |
| unsaved | `●` (`TextBlock.dot`) |
| shiny | `★` gold |
| arrow in a flow | `➜` `#8A96A3` |

Sizes: 16 / 20 / 24 px for small art, 40×30 (×1, ×1.5, ×2) for Pokémon icons. **No emoji in new UI**; the `🔑` for key
items without art is a known exception.

## 6. Interaction and accessibility

- Click targets ≥ 23 px high (the classic control height); icon-only buttons ≥ 24×24 and carry `ToolTip.Tip`.
- **Every option has a hover description** (`ToolTip.Tip` from the resx; `TooltipTests` enforces it for options and
  table fields). A tooltip explains; it never holds information that is needed to act.
- Focus: the accent border (`#0078D7`) on inputs and buttons; never remove a focus visual without another.
- One main action per block = `Button.accent` (blue frame, SemiBold). Destructive actions (start over, delete, put all
  back) are separate, ask first, and the irreversible ones ask twice.
- **No spoilers by default**: abilities, trainer teams and moves stay behind *Show …* check boxes that are off on every
  open; the randomization log only on request.
- Lists bound to `ItemsSource` next to a two-way `SelectedIndex` are built once and never replaced (the ComboBox trap).
- Every user-facing string lives in `Strings.resx` **and** `Strings.es.resx`.

## 7. Layout

Test widths: **1600**, **1280** and **1024** px wide windows (the harness renders 1600×1000; resize the window for the others). Pages use
`AdaptiveColumnsPanel` (1–3 columns of `Border.card`); side lists have fixed widths (360 px trainers, 404 px save,
440 px Pokédex) and the detail takes the rest. Tab strips never wrap. Nothing may overflow horizontally at 1024 px:
wrap, or put the extra controls on their own row.

## 8. Structure (reorganized 2026-11-09 with the user — ask before moving anything)

Where a new page or option goes:

| Place | Holds |
|---|---|
| **Proyecto** → Generar · Archivos · Mods · Historial | what is built (base ROM, seed, output, save adaptation), the export/import files, mods, versions |
| **Opciones de la ROM** → side list Pokémon · Movimientos · Encuentros · Entrenadores · Objetos · Reglas | everything that changes the game; each subject has **Aleatorio** (the randomizer's options: need a new seed), **Ajustes** (this application's, written when the ROM is built) and **Editor avanzado** (per-entry editors) — only the ones it has |
| **Partida** → Pokémon · Mochila · Entrenador · Locke · Pokédex | everything in the save; undo there is the save's |

Rules agreed with the user:
- **Nothing appears twice.** One way to reach a thing; one copy of a hint (the Ajustes sub-tab line says "written at build" once, cards do not repeat it); a shortcut on the start screen opens the editor's page, it is not a second page.
- **Never three rows of tabs**: a third level becomes a side list (`TabStripPlacement="Left"`, class `narrow-side`).
- An option acts where it is shown: something that acts when randomizing sits in Aleatorio even if it is ours (e.g. *Qué objetos pueden salir*), something written at build sits in Ajustes.
- Randomizer options are grouped in **titled cards** (`UprOptionsViewModel.SectionsByGroup`; leftovers land in *Otras*) that flow in balanced columns (`c:ColumnFlowPanel MinColumnWidth=340 MaxColumns=3 ColumnSpacing=12 KeepColumnWidth`); our cards sit in a `WrapPanel` with a 12 px gap, top-aligned. Templates live in `Styles/OptionGroups.axaml`.
- Options that come once per kind of trainer are **one table** (rows = options, columns = Normales · Importantes · Jefes).
- Long paths and ids go in a **tooltip**, not in a header line (save header, editor top bar, *Tu partida*).
- Lists that can be long and are secondary (the save's problems) are **folded** (`Expander`) so the main content keeps its height.
- Side-by-side detail columns use `AdaptiveColumnsPanel`, so they stack at 1024 px instead of squeezing controls to nothing.

## 9. Checklist

- [ ] Colours only as `DynamicResource` tokens (C#: `Tokens`); type backgrounds with `TypeColors.Foreground` text.
- [ ] Checked in dark **and** light; one `primary` per screen; sentence case.
- [ ] Text ≥ 4.5:1 on its background (compute it, do not guess); no `modified`/`success` as small text.
- [ ] Spacing from 2 / 4 / 6 / 8 / 12 / 16 / 24.
- [ ] 13 px body; nothing new under 11 px; ≤ 3 weights.
- [ ] Pokémon look reused from `PokemonLook.axaml`, no local copies of its styles.
- [ ] Game art or the glyph table for icons; sprites centred and pixel-sharp; no new emoji.
- [ ] Tooltip on every option and icon-only button; strings in both resx files.
- [ ] Spoiler content hidden by default; destructive actions separated and confirmed.
- [ ] Checked at 1600, 1280 and 1024 px without horizontal overflow, controls not squeezed.
- [ ] In the right place of the structure (§8); nothing shown twice; no third row of tabs.
