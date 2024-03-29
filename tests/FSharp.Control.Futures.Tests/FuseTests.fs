module FSharp.Control.Futures.Tests.FuseTests

open Expecto

open FSharp.Control.Futures
open FSharp.Control.Futures.Internals

// TODO: Add messages

let fuseThrowsReady = test "Future.fuse throws FutureFuseReadyException if polled after returning Ready" {
    let sourceFut = Future.ready 1
    let fusedFut = Future.fuse sourceFut

    let firstPoll = Future.poll mockContext fusedFut
    Expect.equal firstPoll (Poll.Ready 1) ""

    Expect.throwsT<FutureFuseReadyException>
        (fun () -> Future.poll mockContext fusedFut |> ignore)
        ""
}

let fuseThrowsTransited = test "Future.fuse throws FutureFuseTransitedException if polled after returning Transit" {
    let transitingFut = Future.ready 1
    let sourceFut =
        Future.create
        <| fun _ctx -> Poll.Transit transitingFut
        <| fun () -> ()

    let fusedFut = Future.fuse sourceFut

    let firstPoll = Future.poll mockContext fusedFut
    Expect.equal firstPoll (Poll.Transit transitingFut) ""

    Expect.throwsT<FutureFuseTransitedException>
        (fun () -> Future.poll mockContext fusedFut |> ignore)
        ""
}

let fuseThrowsCancelled = test "Future.fuse throws FutureFuseCancelledException if polled after being cancelled" {
    let sourceFut = Future.ready 1
    let fusedFut = Future.fuse sourceFut

    fusedFut |> Future.drop

    Expect.throwsT<FutureFuseCancelledException>
        (fun () -> Future.poll mockContext fusedFut |> ignore)
        ""
}

[<Tests>]
let tests =
    testList "Future.fuse" [
        fuseThrowsReady
        fuseThrowsTransited
        fuseThrowsCancelled
    ]
