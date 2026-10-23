using System.Windows;
using System.Runtime.CompilerServices;

// Windows-only view tests borrow the compiled card boundaries without starting AppHost,
// global listeners or inference. Keep these presentation methods internal in the product API.
[assembly: InternalsVisibleTo("Cotabby.App.Wpf.Tests")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
