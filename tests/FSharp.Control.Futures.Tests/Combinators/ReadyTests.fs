module FSharp.Control.Futures.Tests.Combinators.Ready

open Expecto
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel
open Xunit


[<Fact>]
let ``Future.ready future returns passed arg``() =
    let x = 12
    let fut = Future.ready x

    let expected = Poll.Ready x
    let actual = Future.poll (Context.mockContext ()) fut

    Expect.equal actual expected "Future.ready return not passed arg or Pending on poll"

[<Fact>]
let ``Future.ready future doesn't call waker``() =
    let fut = Future.ready ()

    let _ = Future.poll (Context.mockContextWithWake (fun () -> Expect.isTrue false "Future.ready shouldn't call waker on poll")) fut
    ()
