namespace FSharp.Control.Futures.Sync

open System.Collections.Concurrent
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


[<Struct>]
type Reply<'a> =
    val private tx: OneShotTx<'a>
    internal new(tx: OneShotTx<'a>) = { tx = tx }
    member this.IsNeedsReply: bool =
        not this.tx.IsClosed
    member this.Reply(reply: 'a): unit =
        this.tx.Send(reply) |> ignore

/// <summary>
/// Multiple Producer Single Consumer (MPSC) synchronisation channel designed like F# MailboxProcessor.
/// Mailbox does not duplicate messages between multiple recipients,
/// but you can repeatedly call <c>Receive</c> without waiting for the previous one,
/// each receiving will receive one of the messages in the beginning waiting order.
/// </summary>
type [<Sealed>] Mailbox<'m> =
    val internal semaphore: Semaphore
    val internal queue: ConcurrentQueue<'m>

    new () = { semaphore = Semaphore(0); queue = ConcurrentQueue<'m>() }

    member this.Receive(): Future<'m> = future {
        do! this.semaphore.Acquire()
        match this.queue.TryDequeue() with
        | true, msg -> return msg
        | false, _ -> return (unreachableS "Mailbox queue is empty but permits acquired")
    }

    member this.Send(msg: 'm): unit =
        do this.queue.Enqueue(msg)
        do this.semaphore.Release()

    member this.SendWithReply<'r>(msgBuilder: Reply<'r> -> 'm): Future<'r> = future {
        let oneshot = OneShot.create ()
        let msg = msgBuilder (Reply(oneshot.AsTx))
        this.Send(msg)
        let! r = oneshot.Await()
        return r
    }
