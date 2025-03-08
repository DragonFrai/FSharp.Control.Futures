module FSharp.Control.Futures.Tests.Combinators.Pending

open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel
open Xunit


[<Fact>]
let ``Future.pending future returns Pending``() =
    let fut: Future<int> = Future.pending
    let ctx = Context.mockContextWithWake (fun () -> Assert.Fail("Future.pending shouldn't call waker on poll"))
    for i in 1..12 do
        let expected = Poll.Pending
        let actual = Future.poll ctx fut
        Assert.Equal(expected, actual)
