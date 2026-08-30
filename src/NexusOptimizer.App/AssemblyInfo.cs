using System.Windows;

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]

// Il gate di test esegue Apply/Revert dell'Optimizer e della Modalità Gaming su
// una chiave di prova: senza questa visibilità la parte che tocca davvero il
// sistema resterebbe l'unica non verificata.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("NexusOptimizer.Tests.Unit")]
