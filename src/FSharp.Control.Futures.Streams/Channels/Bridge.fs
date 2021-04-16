namespace FSharp.Control.Futures.Streams.Channels

open System
open System.Collections.Generic
open FSharp.Control.Futures
open FSharp.Control.Futures.Streams


type BridgeChannel<'a>() =
    let mutable isDisposed = false
    let mutable isCancelled = false
    let mutable msgQueue: Queue<'a> = Queue()
    let mutable context: Context voption = ValueNone
    let syncLock = obj()

    member inline internal _.TryDequeue() = msgQueue.TryDequeue()
    member internal _.SyncObj: obj = syncLock
    member inline private this.EnqueueIfNoCancelled(msg) =
        if (not isCancelled) && (not (obj.ReferenceEquals(msgQueue, null))) then msgQueue.Enqueue(msg)


    interface IChannel<'a> with

        member this.Send(msg) =
            lock syncLock ^fun () ->
                if isDisposed then raise (ObjectDisposedException "Use after dispose")
                match context with
                | ValueNone -> this.EnqueueIfNoCancelled(msg)
                | ValueSome context' ->
                    this.EnqueueIfNoCancelled(msg)
                    context'.Wake()
                    context <- ValueNone

        member this.Dispose() =
            lock syncLock ^fun () ->
                isDisposed <- true
                match context with
                | ValueNone -> ()
                | ValueSome context -> context.Wake()

        member this.PollNext(context') =
            lock syncLock ^fun () ->
                let (hasMsg, x) = msgQueue.TryDequeue()
                if hasMsg
                then
                    if isCancelled && msgQueue.Count = 0
                    then msgQueue <- Unchecked.defaultof<_>
                    StreamPoll.Next x
                else
                    if isDisposed
                    then StreamPoll.Completed
                    else
                        if context.IsSome then invalidOp "Call wake-up on waiting"
                        context <- ValueSome context'
                        StreamPoll.Pending

        member this.Cancel() =
            lock syncLock ^fun () ->
                isCancelled <- true
                msgQueue <- Unchecked.defaultof<_>
                // TODO: impl
                do ()


[<RequireQualifiedAccess>]
module Bridge =

    let create<'a> () = new BridgeChannel<'a>() :> IChannel<_>

    let createPair<'a> () = create<'a> () |> Channel.asPair
