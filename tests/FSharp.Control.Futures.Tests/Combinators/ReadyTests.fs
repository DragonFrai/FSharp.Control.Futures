module FSharp.Control.Futures.Tests.Combinators.Ready

open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel
open Xunit


[<Fact>]
let ``func ~ class``() =
    Assert.True(Future.ready 12 :? Futures.Ready<int>)

[<Fact>]
let ``Future.ready future returns passed arg``() =
    let x = 12
    let fut = Future.ready x

    let expected = Poll.Ready x
    let actual = Future.poll (Context.mockContext ()) fut

    Assert.Equal(expected, actual)

[<Fact>]
let ``Future.ready future doesn't call waker``() =
    let fut = Future.ready ()

    let _ = Future.poll (Context.mockContextWithWake (fun () -> Assert.Fail("Future.ready shouldn't call waker on poll"))) fut
    ()
