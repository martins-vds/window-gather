---
name: Window Gather website
description: A pale-blue reading workspace grounded in the real Windows interface.
colors:
  paper: "#eef5fb"
  ink: "#102c44"
  muted: "#405b71"
  accent: "#006582"
  line: "#b7ccdc"
  white: "white"
typography:
  display:
    fontFamily: '"Segoe UI Variable", "Segoe UI", system-ui, sans-serif'
    fontSize: "clamp(2.8rem, 4.9vw, 4.9rem)"
    fontWeight: 650
    lineHeight: 1.12
    letterSpacing: "-.035em"
  headline:
    fontFamily: '"Segoe UI Variable", "Segoe UI", system-ui, sans-serif'
    fontSize: "clamp(2rem, 3.4vw, 3.1rem)"
    fontWeight: 650
    lineHeight: 1.12
    letterSpacing: "-.025em"
  title:
    fontFamily: '"Segoe UI Variable", "Segoe UI", system-ui, sans-serif'
    fontSize: "1.2rem"
    fontWeight: 700
    lineHeight: 1.12
  body:
    fontFamily: '"Segoe UI Variable", "Segoe UI", system-ui, sans-serif'
    fontSize: "16px"
    fontWeight: 400
    lineHeight: 1.65
  lead:
    fontFamily: '"Segoe UI Variable", "Segoe UI", system-ui, sans-serif'
    fontSize: "1.25rem"
    fontWeight: 400
    lineHeight: 1.65
  small:
    fontFamily: '"Segoe UI Variable", "Segoe UI", system-ui, sans-serif'
    fontSize: ".85rem"
    fontWeight: 400
  code:
    fontFamily: "Consolas, monospace"
    fontSize: ".9em"
rounded:
  control: "5px"
spacing:
  compact: "12px"
  paragraph: "22px"
  heading: "28px"
  stacked: "32px"
  section-mobile: "48px"
  section-desktop: "72px"
components:
  download:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.white}"
    rounded: "{rounded.control}"
    padding: "16px 22px"
  download-hover:
    backgroundColor: "#004c63"
    textColor: "{colors.white}"
  navigation-link:
    textColor: "{colors.ink}"
  text-link:
    textColor: "{colors.accent}"
  keyboard-key:
    backgroundColor: "{colors.white}"
    textColor: "{colors.ink}"
    padding: "2px 5px"
  code-block:
    backgroundColor: "{colors.ink}"
    textColor: "#f3f8fc"
    rounded: "{rounded.control}"
    padding: "20px"
---

# Design System: Window Gather website

## Overview

**Creative North Star: "Windows Reading Workspace"**

A pale-blue reading workspace uses Windows system typography, deep ink text
and restrained cyan-teal actions. Its identity is native-adjacent, not a
replacement design for the Windows executable. Real application imagery
provides product evidence; the surrounding website remains light and readable.

The visual system favors generous section spacing, clear heading hierarchy,
underlined links and thin dividing lines. Most surfaces are flat; a tonal
contributor panel and the lifted application image provide bounded contrast.

**Key Characteristics:**
- Pale-blue canvas with ink text and cyan-teal links.
- Windows system typography with fluid, tightly set headings.
- Open reading sections, thin rules and restrained corners.
- Real native imagery, separated from website styling.

Scope: only `docs\site`. Evidence is `style.css`, the opening contract and markup
in `index.html`, root `PRODUCT.md`, and the supplied desktop/mobile site captures
in `artifacts`. CSS is authoritative for values; screenshots confirm composition.
The five-block opening contract describes this index page, not a universal
layout for every document. No root or native design rules are replaced.

## Colors

A cool, low-chroma reading palette gives the darker action color a clear role.
Frontmatter preserves the source names; prose explains their use.

### Primary
- **Cyan-teal accent** (`accent`): underlined links, the download surface,
  numbered-list markers and keyboard focus outlines.

### Neutral
- **Pale-blue paper** (`paper`): the light page canvas.
- **Deep Windows ink** (`ink`): primary text, brand and navigation; also the
  inverse background for document code blocks.
- **Reading blue-grey** (`muted`): lead copy, supporting instructions,
  captions and footer text.
- **Divider blue** (`line`): section rules, table borders and keyboard-key edges.
- **Control white** (`white`): download text, keyboard-key backgrounds and
  the revealed skip link.

The contributor panel uses a locally defined darker pale-blue fill
(`#d6e9f4`). It is a component treatment, not a second accent or a reusable
palette scale. Link hover darkens to `#003f52`; download hover uses its separate
frontmatter variant.

### Named Rules
**The Reading Canvas Rule.** Keep the website light; the dark application capture is evidence, not the website's theme.

## Typography

**Display Font:** Segoe UI Variable, then Segoe UI, system-ui and sans-serif.
**Body Font:** The same Windows-oriented stack.
**Label/Mono Font:** Labels retain the body stack; document code uses Consolas
with a monospace fallback.

**Character:** A single system family connects the site to Windows without
imitating native controls. Fluid headings are compact and balanced; body copy
has a more open reading rhythm. The stack permits platform fallbacks; it does
not guarantee that Segoe UI Variable is installed.

### Hierarchy
- **Display:** the fluid first-level heading role in frontmatter.
- **Headline:** the fluid second-level section heading role in frontmatter.
- **Title:** third-level headings use the smaller bold role.
- **Body:** normal prose uses the root size and line height, with a maximum
  paragraph measure of (70ch) and bottom spacing from `spacing.paragraph`.
- **Lead:** introductory copy uses the larger body role, muted color and a
  shorter measure of (37ch).
- **Supporting labels:** navigation and download notes use (.95rem);
  small copy, captions and footer use the `small` size. Captions override
  line height to (1.5); small advisory copy has a measure of (42ch).
- **Brand and action emphasis:** the brand is (1.25rem, 700); download and
  emphasized text links are (650); contributor links are (600).

The document layout defines local heading overrides: first-level headings
(2.8rem), second-level headings (1.9rem), and mobile first-level headings
(2.3rem). These are document-specific, not replacements for the fluid index ramp.
Headings use balanced wrapping; first- and second-level heading margins share
the `heading` spacing step. Keyboard keys scale to (.85em) in the prose font.

### Named Rules
**The Windows Voice Rule.** Use the existing system-font stack for website headings and prose; reserve the distinct monospace stack for code.

## Layout

The header, main content and footer share a centered container:
`min(1240px, calc(100% - 96px))`. At the sole width breakpoint
(`max-width: 760px`), it becomes `calc(100% - 40px)`, leaving (20px) on each side.
Navigation wraps rather than hiding behind a menu.

Recurring section padding uses the desktop section step, reduced to the mobile
section step. The extracted spacing entries are recurring values in this
stylesheet, not an imposed arithmetic scale; component padding remains explicit.
Document pages have a narrower measure (76ch), with outer block margins
(60px / 80px), becoming (40px / 60px) on mobile.

The following are observed index-page compositions, not global prescriptions:
- Opening: equal columns, centered alignment and a (56px) gap; mobile stacks
  content with the `stacked` gap and (40px / 48px) block padding.
- Quick-start list: three columns with a (46px) gap; mobile becomes one column
  with a (20px) gap. Bold accent-colored numbering is retained.
- Recovery: a (1.15fr / 1fr) split with a (90px) gap; mobile stacks the aside.
- Contributor panel: equal columns with a (70px) gap and (48px) padding;
  mobile stacks with the `stacked` gap and (28px / 22px) padding.

Document preformatted blocks and tables scroll horizontally when needed.
Inline document code wraps; preformatted code preserves its lines.
Images retain their aspect ratio and do not exceed their container.

## Elevation & Depth

The website is predominantly flat, using thin rules and a tonal fill to
separate content rather than repeated elevated cards. The application capture
alone receives an ambient shadow. No generic elevation ladder is implemented.

### Shadow Vocabulary
- **Application proof:** `box-shadow: 0 20px 40px #102c4426`; recorded as
  an image-specific treatment, not a reusable surface token.

### Named Rules
**The Bounded Depth Rule.** Preserve the current distinction between flat reading sections, the tonal contributor panel and the lifted application image.

## Shapes

Straight section rules and the rectangular contributor panel keep the reading
structure crisp. Download controls and code blocks share `rounded.control`.
The application image has locally softened corners (6px); keyboard keys use
(3px); focused links use (2px). These separate treatments do not establish a
general radius ladder. Dividers and table edges are (1px); keyboard-key bottom
edges are (2px).

## Components

### Buttons
The website's primary action is a link styled as a compact, confident control,
not a form button.
- **Primary:** download frontmatter colors, padding and radius; inline-flex
  alignment with a (32px) gap between label and downward arrow, weight (650).
- **Hover:** the download-hover variant, without a transition or translation.
- **Focus:** the shared link outline (3px solid accent), offset (5px), with
  the focused-link corner treatment (2px).
- No disabled, active, secondary-button or form-input system is defined.

### Navigation
Text-first navigation uses underlined ink links at (.95rem), becoming darker
on hover. Desktop header content is distributed horizontally; at the width
breakpoint, the header stacks with a (12px) gap. Navigation remains visible
and wrapping, with link gaps (28px) on desktop and (20px) on mobile.

### Reading Links and Skip Link
Normal links use the accent color with underline offset (.2em).
Emphasized reading links use weight (650); contributor links use (600) and
flex alignment to separate text from the arrow. All share the same visible
focus outline. The skip link sits above the viewport at rest and becomes
visible near the top-left when focused, on white with (.6rem) padding.

### Containers
Reading sections use top borders rather than enclosed cards.
The contributor panel uses the local pale-blue fill, rectangular edges,
no shadow and the responsive padding documented in Layout. It is not evidence
of a generic card library.

### Keyboard Keys
Keys retain the prose family, use white backgrounds, divider-colored borders,
the locally small corners and frontmatter padding. They remain on one line;
the thicker lower border supplies a subtle keycap shape.

### Document Code and Tables
Code blocks use the inverse ink surface, light code text, shared control radius
and frontmatter padding. Code uses the distinct monospace role. Tables use
collapsed divider-colored borders, left-aligned cells, top alignment and
(10px) cell padding; wide content scrolls rather than shrinking the text.

### Application Proof
The complete real capture is linked to its full-size image, retains intrinsic
proportions, and carries the image-specific shadow and corners.
Its muted caption identifies the simulated preview. A single (600ms)
`cubic-bezier(.16, 1, .3, 1)` clipping reveal runs only when reduced motion is
not requested. This is not a global animation standard.

## Do's and Don'ts

### Do:
- **Do** keep the pale-blue canvas, ink hierarchy and cyan-teal link roles.
- **Do** preserve Windows system typography and readable prose measures.
- **Do** retain underlines, the keyboard-visible focus outline and the skip link.
- **Do** keep real application imagery distinct from website components.
- **Do** preserve source order when the existing grids stack on mobile.

### Don't:
- **Don't** convert the dark application screenshot into a website dark-theme mandate.
- **Don't** invent form controls, generic cards or states absent from the stylesheet.
- **Don't** promote index-page composition or image-only treatments into universal rules.
- **Don't** treat synthesized sidecar color ramps as shipped palette tokens.
- **Don't** apply this website record to the native executable.
