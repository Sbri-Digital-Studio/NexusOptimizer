# Nexus Optimizer — brand direction

## Aurora Blue

Nexus Optimizer usa una direzione visiva calma e tecnica: il prodotto deve comunicare controllo,
non paura. Il colore accento è blu elettrico (`#4F8CFF` in dark mode, `#2F6FE0` in light
mode); il verde (`#34C759`) è riservato a stati realmente sicuri. Il rosso compare solo
quando una metrica documentata supera una soglia certa.

| Ruolo | Token |
|---|---|
| Sfondo dark | `#101418` |
| Superficie card | `#171C22` |
| Testo principale | `#E8ECF1` |
| Testo secondario | `#9AA6B2` |
| Accento | `#4F8CFF` |
| Stato safe | `#34C759` |

Tipografia: Segoe UI Variable Display, fallback Segoe UI. Geometria: card 10–14 px,
spaziatura 4/8/12/16/24/32, grafici lineari senza effetti decorativi. Le icone runtime
sono vettoriali locali in `src/NexusOptimizer.App/Services/AppIcons.cs`; questo evita richieste
online e mantiene l'app leggera e nitida a ogni DPI.

## Asset Canva

Il marchio editabile è stato generato in Canva e verrà aggiornato con il nome Nexus Optimizer:

- Logo editabile: progetto Canva interno (link non pubblicato)
- Anteprima condivisibile: progetto Canva interno (link non pubblicato)

Titolo del concept: **Vector Logo of Nexus Optimizer with Clean Lines**. Il simbolo compatto è la
fonte per l'icona applicazione; la lock-up orizzontale è destinata a installer, sito e
documentazione. Le schermate WPF non dipendono dall'asset remoto: l'icona locale resta
la fonte di verità in esecuzione offline.

## Regole di uso

1. Non usare gradienti decorativi o badge rossi per attirare clic.
2. Non descrivere dati non rilevati: usare `—` o “n.d.”.
3. Ogni grafico deve indicare l'unità e la finestra temporale.
4. Il logo non va usato per suggerire capacità antivirus o “boost” non misurati.
