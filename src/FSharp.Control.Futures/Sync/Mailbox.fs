namespace FSharp.Control.Futures.Sync

open System.Collections.Concurrent
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


exception MailboxMultipleReceiveAtSameTimeException

[<Struct>]
type Reply<'a> =
    val private tx: IOneShotTx<'a>
    internal new(tx: IOneShotTx<'a>) = { tx = tx }
    member this.IsNeedsReply: bool =
        not this.tx.IsClosed
    member this.Reply(reply: 'a): unit =
        this.tx.Send(reply) |> ignore

/// <summary> Multiple Producer Single Consumer (MPSC) synchronisation channel designed like F# MailboxProcessor </summary>
/// <remarks> It is planned that this will be the only type of channels within the framework of the usual Future.
/// More specialized channels will be included in FSharp.Control.Futures.Streams </remarks>
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

    member this.Post(msg: 'm): unit =
        do this.queue.Enqueue(msg)
        do this.semaphore.Release()

    member this.PostWithReply<'r>(msgBuilder: Reply<'r> -> 'm): Future<'r> = future {
        let oneshot = OneShot.create ()
        let msg = msgBuilder (Reply(oneshot))
        this.Post(msg)
        let! r = oneshot.Await()
        return r
    }
