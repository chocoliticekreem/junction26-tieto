# Slip

Slip is a small paper receipt with a torn top, a dog-eared corner and the same zigzag bottom as the trust receipt. He is the careful auditor behind Receipts: honest, a bit nerdy, never smug. He holds the stamp but never uses it on his own; a named human decides.

Source: `dashboard/components/mascot.tsx` (`<Slip pose size />`, `<SlipMark />`). Inline SVG in `currentColor` plus the theme variables (`--paper`, `--primary`, `--ok-foreground`, `--bad-foreground`, `--info`, `--warn-foreground`), so he follows light and dark without extra assets. Favicon: `dashboard/app/icon.svg`.

## Poses

| Pose | What he does | Where |
|---|---|---|
| `idle` | Holds the stamp at his side, blinks, bobs | Trust receipt while the verdict waits on a human |
| `reviewing` | Magnifying glass over a printed line | Queue header next to "N need you"; Product context page |
| `stamp-blocked` | Presses a red stamp, worried brows | Trust receipt and decision page when a defect is proven |
| `stamp-signed` | Stamp raised, green check on the slip, grin | Trust receipt and decision page after a recorded decision |
| `thinking` | Hand on chin, A / B bubble | Decision page before the choice; trust receipt for `decision-A-B` |
| `empty` | Eyes closed, stamp put down | Empty queue, "Nothing needs you", 404 |
| `pinning` | Pushpin through a small sheet | Reserved for pinned old-behaviour tests |

Files here: `slip-<pose>.png` (1024 px, transparent), `slip-mark.png` (head only), `slip-hero.png` (2048 x 1152, Slip beside a signed receipt, for the deck cover), `contact-sheet.png` (all poses, light theme).

## Usage rules

- One Slip per screen. The header mark does not count.
- Never inside findings, evidence, logs or anything a reviewer cites as proof. He decorates the frame, not the record.
- Never joking about a blocked change: `stamp-blocked` is worried, not amused, and no pose smiles next to a proven defect.
- Pass `decorative` when adjacent text already says what is happening; otherwise he is `role="img"` with a plain label.
- Motion stays subtle (blink, bob, one stamp press) and is off under `prefers-reduced-motion`. Give stamp poses the same `delay` as the stamp they accompany so the press lands with it.
- Keep him in currentColor and theme variables. No new colours, no gradients, no outlines thicker than 2.5 units in the 72-unit view box.
