namespace FSharp.Control.Futures.Examples.Docs

open Xunit
open FSharp.Control.Futures


module CreatingCombinators =

    [<Fact>]
    let ``Creating``() =
        let ready = Future.ready "Hello, world!"
        let unit' = Future.unit'
        let pending = Future.pending<string>
        let lazy' = Future.lazy' (fun () -> 2 + 3)
        ()

    [<Fact>]
    let ``Combining``() =
        let map = Future.map (fun n -> n.ToString()) (Future.ready 12)
        let unitFuture = Future.ignore (Future.ready 12)
        let merge = Future.merge (Future.sleepMs 1000) (Future.sleepMs 500)
        let first = Future.first (Future.sleepMs 1000) (Future.sleepMs 500)
        let join = Future.join (Future.ready (Future.ready 12))
        let catch = Future.catch (Future.lazy' (fun () -> failwith "exception"))
        ()







        ()

