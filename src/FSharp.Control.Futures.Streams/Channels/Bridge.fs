namespace FSharp.Control.Futures.Streams.Channels

open System
open System.Collections.Generic
open FSharp.Control.Futures
open FSharp.Control.Futures.Streams


type BridgeChannel<'a>() =
    let mutable isClosed = false
    let msgQueue: Queue<'a> = Queue()
    let mutable context: Context voption = ValueNone
    let syncLock = obj()

    member inline internal _.TryDequeue() = msgQueue.TryDequeue()
    member internal _.SyncObj: obj = syncLock

    interface IChannel<'a> with

        member this.Send(msg) =
            lock syncLock ^fun () ->
                if isClosed then raise (ObjectDisposedException "Use after dispose")
                match context with
                | ValueNone -> msgQueue.Enqueue(msg)
                | ValueSome context' ->
                    msgQueue.Enqueue(msg)
                    context'.Wake()
                    context <- ValueNone

        member this.PollNext(context') =
            lock syncLock ^fun () ->
                let (hasMsg, x) = msgQueue.TryDequeue()
                if hasMsg
                then StreamPoll.Next x
                else
                    if isClosed
                    then StreamPoll.Completed
                    else
                        if context.IsSome then invalidOp "Call wake-up on waiting"
                        context <- ValueSome context'
                        StreamPoll.Pending

        member this.Dispose() =
            lock syncLock ^fun () ->
                if isClosed then invalidOp "Double dispose"
                isClosed <- true
                match context with
                | ValueNone -> ()
                | ValueSome context -> context.Wake()

[<RequireQualifiedAccess>]
module Bridge =

    let create<'a> () = new BridgeChannel<'a>() :> IChannel<'a>

    let createPair<'a> () = create<'a> () |> Channel.asPair
