module FSharp.Control.Futures.Tests.Sync.IVar

open System
open Expecto
open FSharp.Control.Futures
open FSharp.Control.Futures.Sync


// [<Fact>]
// let ``IVar write before read``() =
//     let ivar = OnceVar<int>()
//
//     IVar.put 12 ivar
//     let x = ivar.Get() |> Future.runBlocking
//
//     Expect.equal x 12 "IVar return illegal value"
//     ()
// }
//
// [<Fact>]
// let ``IVar read before write``() =
//     let ivar = OnceVar<int>()
//     let readFut = IVar.get ivar
//     let writeFut = Future.lazy' (fun () -> IVar.put 12 ivar)
//
//     let fut = Future.merge readFut writeFut
//
//     let x, _ = fut |> Future.runBlocking
//
//     Expect.equal x 12 "IVar return illegal value"
//     ()
// }
//
// [<Fact>]
// let ``IVar read exn``() =
//     let ivar = OnceVar<int>()
//     let readFut = IVar.get ivar
//     let writeFut = Future.lazy' (fun () -> IVar.putExn (Exception("error")) ivar)
//
//     let fut = Future.merge readFut writeFut
//
//     try
//         let x, _ = fut |> Future.runBlocking
//         failwith "Error not writed"
//     with
//     | e ->
//         ()
// }
//
// [<Tests>]
// let tests =
//     testList "IVar" [
//         ivarWriteBeforeRead
//         ivarReadBeforeWrite
//         ivarReadExn
//     ]
