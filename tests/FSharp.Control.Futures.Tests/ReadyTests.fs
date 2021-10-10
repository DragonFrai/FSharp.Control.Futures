module FSharp.Control.Futures.Tests.ReadyTests

open Expecto
open FSharp.Control.Futures
open FSharp.Control.Futures.Core


let readyValueTest = test "AsyncComputation.ready future returns passed arg" {
    let x = 12
    let fut = Future.ready x

    let expected = Poll.Ready x
    let actual1 = Future.poll mockContext fut
    let actual2 = Future.poll mockContext fut

    Expect.equal actual1 expected "AsyncComputation.ready return not passed arg or Pending on first poll"
    Expect.equal actual2 expected "AsyncComputation.ready return not passed arg or Pending on second poll"
}

let readyWakerTest = test "AsyncComputation.ready future doesn't call waker" {
    let fut = Future.ready ()

    let _ = Future.poll (mockContextWithWake (fun () -> Expect.isTrue false "AsyncComputation.ready shouldn't call waker on first poll")) fut
    let _ = Future.poll (mockContextWithWake (fun () -> Expect.isTrue false "AsyncComputation.ready shouldn't call waker on second poll")) fut
    ()
}


[<Tests>]
let tests =
    testList "Ready" [
        readyValueTest
        readyWakerTest
    ]
