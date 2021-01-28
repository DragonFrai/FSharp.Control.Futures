module FSharp.Control.Futures.Channels

open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading.Channels


type ISender<'T> =
    abstract member Send: 'T -> Future<unit>

type IReceiver<'T> =
    abstract member Receive: unit -> Future<'T>
    // GetSender ?

type IChannel<'T> =
    inherit ISender<'T>
    inherit IReceiver<'T>

[<RequireQualifiedAccess>]
module Channel =
    let receive (receiver: IReceiver<'a>) =
        receiver.Receive()

    let send (msg: 'a) (sender: ISender<'a>) =
        sender.Send(msg)

// ------------
// Channels
// ------------

module rec QueueChannel =

    type ReceiveFuture<'a>(channel: QueueChannel<'a>) =
        inherit FSharpFunc<Waker, Poll<'a>>()
        member val Waker = ValueNone with get, set
        member val Value = ValueNone with get, set
        override this.Invoke(waker') =
            match this.Value with
            | ValueSome v -> Ready v
            | ValueNone ->
                let deqRes = channel.TryDequeue()
                match deqRes with
                | true, msg ->
                    this.Value <- ValueSome msg
                    Ready msg
                | false, _ ->
                    this.Waker <- ValueSome waker'
                    Pending

    type QueueChannel<'a>() =
        let msgQueue: ConcurrentQueue<'a> = ConcurrentQueue()
        let waiters = ConcurrentQueue<ReceiveFuture<'a>>()

        member internal _.TryDequeue = msgQueue.TryDequeue

        interface IChannel<'a> with
            member this.Send(msg: 'a): Future<unit> =
                future {
                    let x = waiters.TryDequeue()
                    match x with
                    | true, waiter ->
                        waiter.Value <- ValueSome msg
                        match waiter.Waker with
                        | ValueSome waker -> waker ()
                        | _ -> ()
                    | false, _ ->
                        msgQueue.Enqueue(msg)
                    ()
                }

            member this.Receive(): Future<'a> =
                let waiter = ReceiveFuture(this)
                waiters.Enqueue(waiter)
                Future.create (waiter.Invoke)


[<AutoOpen>]
module Channels =

    [<RequireQualifiedAccess>]
    module Channel =
        let create () = QueueChannel.QueueChannel()
