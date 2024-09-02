module FSharp.Control.Futures.Tests.NeverTests

open Expecto
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel
open Xunit


[<Fact>]
let ``Future.never future returns Pending``() =
    let fut: Future<int> = Future.never
    let ctx = Context.mockContextWithWake (fun () -> Expect.isTrue false "Future.never shouldn't call waker on poll")
    for i in 1..12 do
        let expected = Poll.Pending
        let actual = Future.poll ctx fut
        Expect.equal actual expected $"Future.never don't return Pending on {i} poll"

