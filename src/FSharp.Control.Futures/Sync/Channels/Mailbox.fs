namespace FSharp.Control.Futures.Sync

open System.Collections.Concurrent
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel
open FSharp.Control.Futures.Sync.Channels


// TODO: Add closing ???
/// <summary>
/// Multiple Producer Multiple Consumer (MPMC) splitting synchronisation channel designed like F# MailboxProcessor.
/// One <c>Mailbox</c> can be shared between multiple sending and multiple receiving tasks.
/// Each <c>Receive</c> call returns unique message and can be used without waiting previous one.
/// It can be used for distribute work between many tasks.
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
        let struct (reply, future) = Reply.Create()
        let msg = msgBuilder reply
        this.Send(msg)
        return! future
    }
