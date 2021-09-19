module FSharp.Control.Futures.Tests.NeverTests

open Expecto
open FSharp.Control.Futures
open FSharp.Control.Futures.Core


let neverValueTest = test "AsyncComputation.never future returns Pending" {
    let fut: IAsyncComputation<int> = AsyncComputation.never

    let expected = Poll.Pending
    let actual1 = AsyncComputation.poll (mockContext) fut
    let actual2 = AsyncComputation.poll (mockContext) fut

    Expect.equal actual1 expected "AsyncComputation.never return Ready on first poll"
    Expect.equal actual2 expected "AsyncComputation.ready return Ready on second poll"
}

let neverWakerTest = test "AsyncComputation.never future doesn't call waker" {
    let fut = AsyncComputation.ready ()

    let _ = AsyncComputation.poll (mockContextWithWake (fun () -> Expect.isTrue false "AsyncComputation.never shouldn't call waker on first poll")) fut
    let _ = AsyncComputation.poll (mockContextWithWake (fun () -> Expect.isTrue false "AsyncComputation.never shouldn't call waker on second poll")) fut
    ()
}


[<Tests>]
let tests =
    testList "Never" [
        neverValueTest
        neverWakerTest
    ]
