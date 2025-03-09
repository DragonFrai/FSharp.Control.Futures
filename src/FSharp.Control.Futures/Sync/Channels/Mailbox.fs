namespace FSharp.Control.Futures.Sync

open System.Collections.Concurrent
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


[<Struct>]
type Reply<'a> =
    val private tx: OneShotSender<'a>

    new(tx: OneShotSender<'a>) = { tx = tx }

    member this.IsNeedsReply: bool =
        not this.tx.IsClosed

    member this.Reply(reply: 'a): unit =
        this.tx.Send(reply) |> ignore

// TODO: Add closing ???
/// <summary>
/// Multiple Producer Multiple Consumer (MPMC) synchronisation channel designed like F# MailboxProcessor.
/// One <c>Mailbox</c> can be shared between multiple sending and multiple receiving tasks.
/// Each <c>Receive</c> call returns unique message and can be used without waiting previous one.
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
        let msg = msgBuilder (Reply(oneshot.Sender))
        this.Send(msg)
        let! r = oneshot.Receive()
        return r
    }
