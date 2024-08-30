namespace FSharp.Control.Futures.Tests
open Expecto
open Xunit


module ExpectoProxy =
    [<Fact>]
    let ``Expecto tests``() =
        Tests.runTestsInAssembly defaultConfig [||]

module Program =
    [<EntryPoint>]
    let main _argv =
        failwith "For run Expecto tests, run relevant xUnit test"
