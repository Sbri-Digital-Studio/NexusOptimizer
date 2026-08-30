# Design System — Nexus Optimizer

Filosofia: professionale, calmo, onesto. Zero colori-spavento. Ruolo colore per STATO, non per marketing.

## Palette (token)
| Token | Dark | Light |
|---|---|---|
| bg.window | #101418 | #F3F5F8 |
| bg.card | #171C22 | #FFFFFF |
| bg.sidebar | #0C1013 | #EAEEF3 |
| text.primary | #E8ECF1 | #171C22 |
| text.secondary | #9AA6B2 | #5B6570 |
| border.subtle | #232B33 | #DCE2E9 |
| accent | #4F8CFF (configurabile) | #2F6FE0 |
| state.green | #34C759 | #1F9D44 |
| state.yellow | #E6A23C | #B7791F |
| state.red | #FF5A5F | #C0392B (SOLO metriche certe) |

## Tipografia
Segoe UI Variable Display; fallback Segoe UI. Scala: Title 26 semibold · Section 17 semibold ·
Body 13 regular · Caption 11 secondary. Numeri metrici tabular-lining (feature set 1).

## Geometria
Corner radius: card 10 · card-grande 14 · pill/button 6. Spacing scale 4/8/12/16/24/32.
Elevazione: ombra morbida (Y+2 blur10 alpha 18%) su card hover soltanto — restiamo sobri.

## Componenti (inventario FASE 1→2)
Implementati: `MetricCard` premium (gauge, valore, dettaglio e sparkline reale), `RadialGauge`
(DrawingContext nativo, animazione 230 ms solo al cambio campione), `SparkGraph`
(polilinea canvas-render, ring-buffer 180 campioni), Sidebar navigabile, Toggle rapido,
header con ricerca e tema Dark/Light/auto-by-config. La palette delle metriche segue il riferimento:
CPU ciano, RAM viola, disco blu, rete teal; i valori restano sempre misurati localmente.
Prossimi FASE 2: `HealthScoreRing` cliccabile con breakdown formula, `SafetyPill` GREEN/YELLOW/RED,
ToggleRow moderno (toggle MDL2 stile), breadcrumb, contextual menus, skeleton loading, animazioni
(200–250 ms cubic ease-out; mai sopra 300 ms; disabilitabili da config.animations).
Icone runtime: geometrie proprie in `Services/AppIcons.cs` (SINGLE SOURCE OF TRUTH),
vettoriali e offline. Il marchio/monogramma Aurora Blue è stato generato in Canva e
documentato in `docs/BRAND.md`; l'eventuale export di una famiglia SVG completa resta
un'attività di design separata, senza introdurre dipendenze remote nell'app.

## Accessibilità
Contrasto AA sulle coppie primarie; focus keyboard visibile (accId pill 2px);
AutomationProperties.Name su card e toggle; supporto High Contrast passthrough; scaling DPI nativo WPF.

## Regole azionabili
1. Il rosso compare SOLO quando una metrica certa supera soglia documentata.
2. Niente gradienti decorativi; il gradiente e' ammesso solo dentro il gauge HealthScore.
3. Ogni numero non ancora misurabile mostra em-dash "—" e caption "disponibile dalla fase X".
4. Le animazioni rispettano `animations=false` in config (accessibility override utente).
5. Minimizzato = polling STOPPED (transparency: indicatore "live" grigio).
