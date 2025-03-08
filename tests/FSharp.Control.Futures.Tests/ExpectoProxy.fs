namespace FSharp.Control.Futures.Tests
open Expecto
open Xunit


module ExpectoProxy =
    [<Fact>]
    let ``Expecto tests``() =
        Tests.runTestsInAssembly defaultConfig [||]
