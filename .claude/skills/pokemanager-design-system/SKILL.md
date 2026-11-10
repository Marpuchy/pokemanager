---
name: pokemanager-design-system
description: Use whenever a view, control, style or visual detail of Pokemanager (Avalonia, .axaml) is created, changed or reviewed, to apply its design system: colours, type colours, spacing, text sizes, icons and interaction rules.
---

# Pokemanager design system

## 1. Context

Pokemanager is a **Windows desktop app** (Avalonia 12, light theme only) used by one player while they play a 3DS
Pokémon game on Citra: randomize and build the ROM, edit the save, follow a Nuzlocke. The window sits next to the
emulator, so it must be **readable at a glance and dense**, like PKHeX / UPR ZX, not like a web page. Mouse and
keyboard, no touch.

Source of truth for the look, in this order:

- `src/Pokemanager.App/Styles/ClassicTheme.axaml`: the classic WinForms-like skin on top of Fluent (compact). Brushes
  `Classic*`.
- `src/Pokemanager.App/Styles/PokemonLook.axaml`: **everything that shows a Pokémon** (party cards, box cells, detail
  header, badges, stat bars, move rows, evolution cards, `MonSheet`). Reuse these classes and keyed templates; **never
  copy their styles into a view** (local styles win and the looks drift apart).
- `src/Pokemanager.App/App.axaml`: `Border.card`, `TextBlock.card-title/.section/.hint/.error/.dot`, `field-label.modified`.
- `src/Pokemanager.App/Services/TypeColors.cs`: the 18 type colours and the text colour on them.

## 2. Colour

Only these values. A new colour needs a reason and a token here first.

| Token | Value | Use | Contrast (WCAG) |
|---|---|---|---|
| `surface` | `#F0F0F0` | window background (`ClassicWindowBrush`) | — |
| `panel` | `#FFFFFF` | tab pages, `Border.card`, inputs | — |
| `button` | `#E1E1E1` | button face | — |
| `ink` | `#000000` / `#1A1A1A` | body text | 21 / 17.4 on panel |
| `ink-muted` | `#6D6D6D` | `TextBlock.hint`, secondary lines | 5.17 on panel, 4.54 on surface, **3.96 on button: not for button text** |
| `title` | `#1C3A70` | `card-title`, `section`, selected outline, level pills | 11.1 on panel |
| `accent` | `#0078D7` | focus, hover border, selection, `Button.accent` frame | 4.50 on panel (limit), 3.95 on surface |
| `accent-hover` / `accent-pressed` | `#E5F1FB` / `#CCE4F7` | button hover / pressed fill | — |
| `border-control` | `#7A7A7A` | input borders | 4.29 on panel ✓ (≥ 3) |
| `border-button` | `#ADADAD` | button borders | 2.24 — the face carries the shape |
| `border-card` | `#D5DFE5` / `#D9D9D9` | card and row rules (decorative only) | 1.35 |
| `modified` | `#E07B00` | modified bars, dots, tags' fill | **3.0 on panel: only for shapes, ≥ bold 14 px or with an icon** |
| `modified-text` | `#7A4B00` | text of "Modified" tags and labels | 7.41 on panel |
| `error` | `#D13438` | `TextBlock.error`, problem frames | 4.93 on panel, 4.33 on surface |
| `success` | `#2E8B57` | `hint.current`, earned ✓ | **4.25 on panel: below 4.5** |
| `disabled-text` | `#A0A0A0` | disabled controls only | 2.61 (WCAG exempts disabled) |
| `gold` | `#FFD700` | shiny star | shape only |

Known contrast debts (do not copy; fix when the file is touched, ask before a sweep):
- `success` text → `#237A4B` (5.31 on panel, 4.66 on surface).
- `modified` used as **text** at 12 px → `modified-text` `#7A4B00`, or `#A85A00` (5.09) if it must stay orange.
- `error` on the grey window → `#C42B2F` (4.94 on surface).

**Type colours** (`TypeColors`, game order: Normal, Fighting, Flying, Poison, Ground, Rock, Bug, Ghost, Steel, Fire,
Water, Grass, Electric, Psychic, Ice, Dragon, Dark, Fairy) are the Showdown palette. Two types = **hard diagonal cut**
(both gradient stops at 0.5). Text on them always comes from `TypeColors.Foreground` (white below luminance 0.62,
`#1A1A1A` above); never hard-code a text colour on a type background. Stat bars use the Showdown stat colours of
`StatFieldViewModel` / `StatBars`; nature marks: raised ▲ red, lowered ▼ blue.

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

Segoe UI (Tahoma, Verdana fallback), **12 px base** everywhere (`Window`, controls). Scale:

| Role | Size / weight |
|---|---|
| display (detail header name, level) | 18–22 SemiBold |
| h1 (page or dialog title) | 16 SemiBold |
| h2 (`card-title`, `section`) | 12 SemiBold, `title` colour |
| body | 12 Normal |
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

- [ ] Colours only from the table above; type backgrounds with `TypeColors.Foreground` text.
- [ ] Text ≥ 4.5:1 on its background (compute it, do not guess); no `modified`/`success` as small text.
- [ ] Spacing from 2 / 4 / 6 / 8 / 12 / 16 / 24.
- [ ] 12 px body; nothing new under 11 px; ≤ 3 weights.
- [ ] Pokémon look reused from `PokemonLook.axaml`, no local copies of its styles.
- [ ] Game art or the glyph table for icons; sprites centred and pixel-sharp; no new emoji.
- [ ] Tooltip on every option and icon-only button; strings in both resx files.
- [ ] Spoiler content hidden by default; destructive actions separated and confirmed.
- [ ] Checked at 1600, 1280 and 1024 px without horizontal overflow, controls not squeezed.
- [ ] In the right place of the structure (§8); nothing shown twice; no third row of tabs.
