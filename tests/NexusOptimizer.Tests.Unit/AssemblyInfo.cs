// La lingua corrente di Locale e' uno stato statico di processo: dopo la Fase 7
// quasi ogni testo mostrato la attraversa. Con le classi di test in parallelo,
// LocaleTests potrebbe cambiare lingua mentre un'altra classe verifica una frase
// tradotta. La suite dura meno di un secondo: la serializzazione costa nulla e
// rende i risultati deterministici.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
