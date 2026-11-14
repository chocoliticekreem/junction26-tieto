# Slip

Slip is a small paper receipt with a torn top, a dog-eared corner and the same zigzag bottom as the trust receipt. He is the careful auditor behind Receipts: honest, a bit nerdy, never smug. He holds the stamp but never uses it on his own; a named human decides.

Source: `dashboard/components/mascot.tsx` (`<Slip pose size />`, `<SlipMark />`). Since the hand-drawn pass he is drawn art, not code: `hand/slip-<pose>.svg` (light inks) and `hand/slip-<pose>-dark.svg` (dark theme), risograph style with two inks (ink blue, vermilion), misregistered fills, grain and a wobble filter, every stroke a filled brush outline. The component swaps the two files with the `dark:` variant. Favicon: `dashboard/app/icon.svg`.

## Poses

| Pose | What he does | Where |
|---|---|---|
| `idle` | Holds the stamp at his side, blinks, bobs | Trust receipt while the verdict waits on a human |
| `reviewing` | Magnifying glass over a printed line | Queue header next to "N need you"; Product context page |
| `stamp-blocked` | Presses a red stamp, worried brows | Trust receipt and decision page when a defect is proven |
| `stamp-signed` | Stamp raised, green check on the slip, grin | Trust receipt and decision page after a recorded decision |
| `thinking` | Hand on chin, A / B bubble | Decision page before the choice; trust receipt for `decision-A-B` |
| `empty` | Eyes closed, stamp put down | Empty queue, "Nothing needs you", 404 |
| `pinning` | Pushpin through a small sheet on a corkboard | Reserved for pinned old-behaviour tests |
| `hero` | Stamps a receipt as it prints | Deck cover |
| `shrug` | Arms out, honest about limits | "Not checked" |
| `waving` | Waves hello | Cover, close |
| `pointing` | Points at a result, clipboard in hand | Presenting a number |
| `celebrating`, `question`, `reading`, `thumbs-up` | Extra acting poses | Slides only |

Files here: the old geometric set (`slip-<pose>.png`, 1024 px; `slip-hero.png`; `contact-sheet.png`). The hand-drawn set is under `hand/`: `slip-<pose>.svg` + `-dark.svg` sources, `slip-<pose>.png` (1600 px, transparent) and `hand/contact-sheet.png`.

## Usage rules

- One Slip per screen. The header mark does not count.
- Never inside findings, evidence, logs or anything a reviewer cites as proof. He decorates the frame, not the record.
- Never joking about a blocked change: `stamp-blocked` is worried, not amused, and no pose smiles next to a proven defect.
- Pass `decorative` when adjacent text already says what is happening; otherwise he is `role="img"` with a plain label.
- Motion stays subtle (blink, bob, one stamp press) and is off under `prefers-reduced-motion`. Give stamp poses the same `delay` as the stamp they accompany so the press lands with it.
- Two inks only: ink blue and vermilion on paper. No gradients. Edit the generator, not the SVGs by hand: regenerate both theme variants together.
