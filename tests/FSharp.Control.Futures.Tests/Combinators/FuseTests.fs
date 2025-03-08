module FSharp.Control.Futures.Tests.Combinators.Fuse

open Xunit
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


// TODO: Add messages

[<Fact>]
let ``Future.fuse throws FutureFuseReadyException if polled after returning Ready``() =
    let sourceFut = Future.ready 1
    let fusedFut = Future.fuse sourceFut

    let firstPoll = Future.poll (Context.mockContext ()) fusedFut
    Assert.Equal(Poll.Ready 1, firstPoll)
    Assert.Throws<FutureFuseReadyException>(
        fun () -> Future.poll (Context.mockContext ()) fusedFut |> ignore
    ) |> ignore
    ()

[<Fact>]
let ``Future.fuse throws FutureFuseTransitedException if polled after returning Transit``() =
    let transitingFut = Future.ready 1
    let sourceFut =
        Future.create
        <| fun _ctx -> Poll.Transit transitingFut
        <| fun () -> ()

    let fusedFut = Future.fuse sourceFut

    let firstPoll = Future.poll (Context.mockContext ()) fusedFut
    Assert.Equal(Poll.Transit transitingFut, firstPoll)
    Assert.Throws<FutureFuseTransitedException>(
        fun () -> Future.poll (Context.mockContext ()) fusedFut |> ignore
    ) |> ignore
    ()

[<Fact>]
let ``Future.fuse throws FutureFuseCancelledException if polled after being cancelled``() =
    let sourceFut = Future.ready 1
    let fusedFut = Future.fuse sourceFut

    fusedFut |> Future.drop

    Assert.Throws<FutureFuseCancelledException>(
        fun () -> Future.poll (Context.mockContext ()) fusedFut |> ignore
    ) |> ignore
