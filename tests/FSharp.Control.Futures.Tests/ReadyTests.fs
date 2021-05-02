module FSharp.Control.Futures.Tests.ReadyTests

open Expecto
open FSharp.Control.Futures


let readyValueTest = test "AsyncComputation.ready future returns passed arg" {
    let x = 12
    let fut = AsyncComputation.ready x

    let expected = Poll.Ready x
    let actual1 = AsyncComputation.poll (mockContext) fut
    let actual2 = AsyncComputation.poll (mockContext) fut

    Expect.equal actual1 expected "AsyncComputation.ready return not passed arg or Pending on first poll"
    Expect.equal actual2 expected "AsyncComputation.ready return not passed arg or Pending on second poll"
}

let readyWakerTest = test "AsyncComputation.ready future doesn't call waker" {
    let fut = AsyncComputation.ready ()

    let _ = AsyncComputation.poll (mockContextWithWake (fun () -> Expect.isTrue false "AsyncComputation.ready shouldn't call waker on first poll")) fut
    let _ = AsyncComputation.poll (mockContextWithWake (fun () -> Expect.isTrue false "AsyncComputation.ready shouldn't call waker on second poll")) fut
    ()
}


[<Tests>]
let tests =
    testList "Ready" [
        readyValueTest
        readyWakerTest
    ]
