module FSharp.Control.Futures.Channels.Bridge

open System
open System.Collections.Concurrent
open System.Collections.Generic
open FSharp.Control.Futures


type ReceiveFuture<'a>(channel: BridgeChannel<'a>, sync: obj) =
    inherit Future<Result<'a, ReceiveError>>()

    member val Waker = ValueNone with get, set
    member val Value: Result<'a, ReceiveError> voption = ValueNone with get, set

    member inline internal this.PutAndWakeNoLock(res: Result<'a>) =
        this.Value <- ValueSome res
        match this.Waker with
        | ValueSome w -> w ()
        | ValueNone -> do ()

    override this.Poll(waker') =
        match this.Value with
        | ValueSome msg -> Ready msg
        | ValueNone ->
            lock channel.SyncObj ^fun () ->
                match this.Value with
                | ValueSome msg -> Ready msg
                | ValueNone ->
                    this.Waker <- ValueSome waker'
                    Pending

and BridgeChannel<'a>() =
    let mutable isClosed = false
    let msgQueue: Queue<'a> = Queue()
    let mutable waiter: ReceiveFuture<'a> voption = ValueNone
    let syncLock = obj()

    member inline internal _.TryDequeue() = msgQueue.TryDequeue()
    member internal _.SyncObj: obj = syncLock

    interface IChannel<'a> with

        member this.Send(msg) =
            lock syncLock ^fun () ->
                if isClosed then raise (ObjectDisposedException "Use after dispose")
                match waiter with
                | ValueNone -> msgQueue.Enqueue(msg)
                | ValueSome waiter' ->
                    waiter'.PutAndWakeNoLock(Ok msg)
                    waiter <- ValueNone

        member this.Receive(): Future<Result<'a, ReceiveError>> =
            lock syncLock ^fun () ->
                if waiter.IsSome then raise (DoubleReceiveException "Double receive of one item")
                let (hasMsg, msg) = msgQueue.TryDequeue()
                if hasMsg
                then Future.ready (Ok msg)
                else
                    if isClosed
                    then Future.ready (Error Closed)
                    else
                        let waiter' = ReceiveFuture(this, syncLock)
                        waiter <- ValueSome waiter'
                        waiter' :> Future<_>

        member this.Dispose() =
            lock syncLock ^fun () ->
                if isClosed then raise (ObjectDisposedException "Double dispose")
                isClosed <- true
                match waiter with
                | ValueNone -> ()
                | ValueSome waiter ->
                    waiter.PutAndWakeNoLock(Error Closed)


let createBridge<'a> () = new BridgeChannel<'a>()

let create<'a> () = createBridge () :> IChannel<'a>

let createPair<'a> () = createBridge<'a> () |> Channel.toPair
