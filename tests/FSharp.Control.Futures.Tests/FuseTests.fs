module FSharp.Control.Futures.Tests.FuseTests

open Expecto

open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel
open Xunit

// TODO: Add messages

[<Fact>]
let ``Future.fuse throws FutureFuseReadyException if polled after returning Ready``() =
    let sourceFut = Future.ready 1
    let fusedFut = Future.fuse sourceFut

    let firstPoll = Future.poll (Context.mockContext ()) fusedFut
    Expect.equal firstPoll (Poll.Ready 1) ""

    Expect.throwsT<FutureFuseReadyException>
        (fun () -> Future.poll (Context.mockContext ()) fusedFut |> ignore)
        ""

[<Fact>]
let ``Future.fuse throws FutureFuseTransitedException if polled after returning Transit``() =
    let transitingFut = Future.ready 1
    let sourceFut =
        Future.create
        <| fun _ctx -> Poll.Transit transitingFut
        <| fun () -> ()

    let fusedFut = Future.fuse sourceFut

    let firstPoll = Future.poll (Context.mockContext ()) fusedFut
    Expect.equal firstPoll (Poll.Transit transitingFut) ""

    Expect.throwsT<FutureFuseTransitedException>
        (fun () -> Future.poll (Context.mockContext ()) fusedFut |> ignore)
        ""

[<Fact>]
let ``Future.fuse throws FutureFuseCancelledException if polled after being cancelled``() =
    let sourceFut = Future.ready 1
    let fusedFut = Future.fuse sourceFut

    fusedFut |> Future.drop

    Expect.throwsT<FutureFuseCancelledException>
        (fun () -> Future.poll (Context.mockContext ()) fusedFut |> ignore)
        ""
