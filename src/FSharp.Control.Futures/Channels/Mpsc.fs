module FSharp.Control.Futures.Channels.Mpsc

open System.Collections.Concurrent
open FSharp.Control.Futures


type ReceiveFuture<'a>(channel: QueueChannel<'a>) =
    inherit FSharpFunc<Waker, Poll<'a option>>()

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

and QueueChannel<'a>() =
    let msgQueue: ConcurrentQueue<'a option> = ConcurrentQueue()
    let waiters = ConcurrentQueue<ReceiveFuture<'a>>()

    member inline internal _.TryDequeue() = msgQueue.TryDequeue()

    interface IChannel<'a> with
        member this.Send(msg: 'a): Future<unit> =
            future {
                let x = waiters.TryDequeue()
                match x with
                | true, waiter ->
                    waiter.Value <- ValueSome (Some msg)
                    match waiter.Waker with
                    | ValueSome waker -> waker ()
                    | _ -> ()
                | false, _ ->
                    msgQueue.Enqueue(Some msg)
                ()
            }

        member this.Receive(): Future<'a option> =
            let waiter = ReceiveFuture(this)
            waiters.Enqueue(waiter)
            FutureCore.create (waiter.Invoke)
