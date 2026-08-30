# Localizzazione IT / EN

Due dizionari nel file parziale `Locale` ([`Locale.cs`](../src/NexusOptimizer.App/Services/Locale.cs)
e [`Locale.En.cs`](../src/NexusOptimizer.App/Services/Locale.En.cs)), **785 chiavi
per lingua**. Una chiave mancante ritorna se stessa: mai testo segnaposto
silenzioso in interfaccia.

## Copertura

L'interfaccia è tradotta **per intero**: navigazione, Dashboard, Smart Clean,
Optimizer, Modalità Gaming, "Il mio PC", Monitor, Processi, Startup Manager,
Privacy Guard, Toolkit, cronologia, Impostazioni, onboarding, avvisi,
aggiornamenti, dialoghi di conferma e messaggi di errore.

Sono tradotti sia i testi statici delle viste sia i **messaggi composti a
runtime**: esiti delle ottimizzazioni con forme singolare/plurale, resoconto del
boost, schede WMI di "Il mio PC", frasi di stato, conferme e avvisi. Gli avvisi
memorizzano *chiavi e argomenti*, non frasi: cambiando lingua si riscrive anche
la cronologia già raccolta.

Restano invariati solo i nomi propri e le sigle tecniche: NEXUS OPTIMIZER, Smart
Clean, Optimizer, Disk Manager, RAM Manager, Privacy Guard, CPU, RAM, GPU, PID,
THREAD, PUBLISHER, LIVE, PC HEALTH SCORE.

## Cambio lingua immediato

I testi statici sono legati a `Locale.Live`, una sorgente osservabile che espone
una revisione:

```xml
Text="{Binding Source={x:Static s:Locale.Live}, Path=Version,
       Converter={StaticResource Loc}, ConverterParameter=chiave}"
```

`Locale.Set` incrementa `Version`: WPF rivaluta i 220 binding e l'interfaccia si
riscrive **senza cambiare pagina**. Prima serviva uscire e rientrare nella
sezione. Le proprietà localizzate dei ViewModel continuano a usare l'evento
`Locale.Changed`.

## Numeri e date seguono la lingua, non il sistema

`Locale.Set` imposta la cultura dell'intero processo (`it-IT` oppure `en-US`),
quindi `2,3 GB` in italiano e `2.3 GB` in inglese — su qualunque Windows. Un PC
di sistema in inglese con Nexus in italiano continua a mostrare la virgola:
**decide la lingua scelta, non il sistema operativo**. Verificato da
`FormatterTests.ItalianCultureIsUsedRegardlessOfSystemLocale` e
`EnglishInterfaceFormatsNumbersInEnglish`.

Il marcatore di dato assente segue la stessa regola: `n.d.` in italiano, `n/a` in
inglese. Il trattino `—` resta un simbolo, uguale ovunque.

## Gate automatici

In [`LocaleTests`](../tests/NexusOptimizer.Tests.Unit/LocaleTests.cs):

- `ItalianAndEnglishCoverTheSameKeys` — fallisce elencando le chiavi presenti in
  una sola lingua.
- `PlaceholdersMatchBetweenLanguages` — confronta i segnaposto `{0}`, `{1}` di
  ogni coppia IT/EN: uno perso fa sparire un numero dal messaggio, uno di troppo
  farebbe fallire `string.Format`.
- `RuntimeMessages_ResolveInBothLanguages` — campione delle chiavi dei messaggi
  composti a runtime, verificate in entrambe le lingue.

In [`ViewSmokeTests`](../tests/NexusOptimizer.Tests.Unit/ViewSmokeTests.cs) tutte
e quindici le viste più la finestra principale vengono caricate davvero, e il
cambio lingua deve incrementare `Locale.Live.Version`.

La suite unit gira **serializzata**
([`AssemblyInfo.cs`](../tests/NexusOptimizer.Tests.Unit/AssemblyInfo.cs)): la
lingua è uno stato statico di processo e con le classi in parallelo un test che
la cambia falserebbe le asserzioni di un altro.

Verificato a runtime: con `language=en` l'applicazione si avvia, apre Dashboard,
Modalità Gaming e Startup Manager **senza un solo errore di binding**.

## Come aggiungere una chiave

1. Inserirla in **entrambi** i dizionari, nella stessa sezione commentata. Una
   chiave duplicata farebbe fallire l'inizializzazione del dizionario all'avvio:
   il gate di parità e quello dei segnaposto la intercettano prima.
2. Nello XAML: `Text="{Binding Source={x:Static s:Locale.Live}, Path=Version, Converter={StaticResource Loc}, ConverterParameter=chiave}"`
   (servono `xmlns:s="clr-namespace:NexusOptimizer.App.Services"` e
   `<s:LocaleKeyConverter x:Key="Loc" />` fra le risorse della vista).
3. Nel codice: `Locale.T("chiave")`, `Locale.F("chiave", [arg0, arg1])` per i
   segnaposto, `Locale.P(n, "chiave.one", "chiave.many")` per numero più parola
   concordata.
4. `dotnet test`.

I log restano in italiano: sono diagnostica per chi sviluppa, non interfaccia.
