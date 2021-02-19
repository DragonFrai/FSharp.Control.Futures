namespace FSharp.Control.Futures.SeqStream.Channels

open System
open System.Collections.Generic
open FSharp.Control.Futures
open FSharp.Control.Futures.SeqStream


type BridgeChannel<'a>() =
    let mutable isClosed = false
    let msgQueue: Queue<'a> = Queue()
    let mutable waker: Waker voption = ValueNone
    let syncLock = obj()

    member inline internal _.TryDequeue() = msgQueue.TryDequeue()
    member internal _.SyncObj: obj = syncLock

    interface IChannel<'a> with

        member this.Send(msg) =
            lock syncLock ^fun () ->
                if isClosed then raise (ObjectDisposedException "Use after dispose")
                match waker with
                | ValueNone -> msgQueue.Enqueue(msg)
                | ValueSome waker' ->
                    msgQueue.Enqueue(msg)
                    waker' ()
                    waker <- ValueNone

        member this.PollNext(waker') =
            lock syncLock ^fun () ->
                let (hasMsg, x) = msgQueue.TryDequeue()
                if hasMsg
                then SeqNext x
                else
                    if isClosed
                    then SeqCompleted
                    else
                        if waker.IsSome then invalidOp "Call wake-up on waiting"
                        waker <- ValueSome waker'
                        SeqPending

        member this.Dispose() =
            lock syncLock ^fun () ->
                if isClosed then invalidOp "Double dispose"
                isClosed <- true
                match waker with
                | ValueNone -> ()
                | ValueSome waker -> waker ()

[<RequireQualifiedAccess>]
module Bridge =

    let create<'a> () = new BridgeChannel<'a>() :> IChannel<'a>

    let createPair<'a> () = create<'a> () |> Channel.asPair
