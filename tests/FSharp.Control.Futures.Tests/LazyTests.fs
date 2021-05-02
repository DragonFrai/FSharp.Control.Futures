module FSharp.Control.Futures.Tests.LazyTests

open Expecto
open FSharp.Control.Futures

let lazyCallFuncOnceTest = test "Future.lazy' correct call passed function order" {
    let checker = OrderChecker()
    let fut = AsyncComputation.lazy' (fun () -> checker.PushPoint(2);)
    do checker.PushPoint(1)

    let _ = AsyncComputation.poll (mockContext) fut
    let _ = AsyncComputation.poll (mockContext) fut

    Expect.sequenceEqual (checker.ToSeq()) [1; 2] <| "Illegal breakpoint order"
    ()
}

let lazyCallWakerTest = test "Future.lazy' doesn't call waker" {
    let fut = AsyncComputation.lazy' (fun () -> 0)

    let _ = AsyncComputation.poll (mockContextWithWake (fun () -> Expect.isTrue false "AsyncComputation.lazy' shouldn't call waker")) fut
    let _ = AsyncComputation.poll (mockContextWithWake (fun () -> Expect.isTrue false "AsyncComputation.lazy' shouldn't call waker")) fut

    ()
}

let lazyValueTest = test "Future.lazy' call passed function once" {
    let x = 12
    let fut = AsyncComputation.lazy' (fun () -> x)

    let expected = Poll.Ready x
    let actual1 = AsyncComputation.poll (mockContext) fut
    let actual2 = AsyncComputation.poll (mockContext) fut

    Expect.equal actual1 expected "AsyncComputation.lazy' return not passed arg or Poll.Pending on first poll"
    Expect.equal actual2 expected "AsyncComputation.lazy' return not passed arg or Poll.Pending on second poll"
}

[<Tests>]
let tests =
    testList "Lazy" [
        lazyCallFuncOnceTest
        lazyCallWakerTest
        lazyValueTest
    ]
