module FSharp.Control.Futures.Tests.ReadyTests

open Expecto
open FSharp.Control.Futures


let readyValueTest = test "Future.ready future returns passed arg" {
    let x = 12
    let fut = Future.ready x

    let expected = Poll.Ready x
    let actual1 = Future.Core.poll (fun () -> do ()) fut
    let actual2 = Future.Core.poll (fun () -> do ()) fut

    Expect.equal actual1 expected "Future.ready return not passed arg or Pending on first poll"
    Expect.equal actual2 expected "Future.ready return not passed arg or Pending on second poll"
}

let readyWakerTest = test "Future.ready future doesn't call waker" {
    let fut = Future.ready ()

    let _ = Future.Core.poll (fun () -> Expect.isTrue false "Future.ready shouldn't call waker on first poll") fut
    let _ = Future.Core.poll (fun () -> Expect.isTrue false "Future.ready shouldn't call waker on second poll") fut
    ()
}


[<Tests>]
let tests =
    testList "Ready" [
        readyValueTest
        readyWakerTest
    ]
