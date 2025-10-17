namespace FSharp.Control.Futures.Mail

open System.Collections.Concurrent
open System.Diagnostics
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel
open FSharp.Control.Futures.Sync


/// <summary>
/// Multiple Producer Multiple Consumer (MPMC) splitting synchronisation channel designed like F# MailboxProcessor.
/// One <c>Mailbox</c> can be shared between multiple sending and multiple receiving tasks.
/// Each <c>Receive</c> call returns unique message and can be used without waiting previous one.
/// It can be used for distribute work between many tasks.
/// </summary>
// TODO: Rename to Unbounded mailbox
type [<Sealed>] Mailbox<'m> =
    val internal semaphore: Semaphore
    val internal queue: ConcurrentQueue<'m>

    new () = { semaphore = Semaphore(0); queue = ConcurrentQueue<'m>() }

    interface IMailbox with
        member this.Count = this.semaphore.AvailablePermits
        member this.Bound = 0
        member this.IsCompleted = this.semaphore.IsClosed

    member this.Write(msg: 'm): unit =
        do this.queue.Enqueue(msg)
        do this.semaphore.Release()

    interface IOutbox<'m> with
        member this.TrySend(msg) = future {
            do (this :> IOutbox<'m>).Push(msg) |> ignore
            return Ok ()
        }

        member this.Send(msg) = future {
            do (this :> IOutbox<'m>).Push(msg) |> ignore
        }

        member this.Push(msg) =
            do this.queue.Enqueue(msg)
            do this.semaphore.Release()
            Ok ()

        member this.Complete() =
            this.semaphore.Close()

    interface IInbox<'m> with
        member this.Pick() =
            match this.semaphore.AcquireNow() with
            | AcquireResult.Ok ->
                match this.queue.TryDequeue() with
                | true, value -> Ok value
                | false, _ -> raise (UnreachableException("Semaphore acquired, but queue is empty"))
            | AcquireResult.NoPermits ->
                Error MailboxPickError.Empty
            | AcquireResult.Closed ->
                Error MailboxPickError.Completed

        member this.TryReceive() = future {
            let! res = this.semaphore.TryAcquire()
            match res with
            | true ->
                match this.queue.TryDequeue() with
                | true, msg -> return Ok msg
                | false, _ -> return (unreachableS "Mailbox queue is empty but permits acquired")
            | false ->
                return Error MailboxError.Completed
        }

        member this.Receive() = future {
            match! (this :> IInbox<'m>).TryReceive() with
            | Ok msg -> return msg
            | Error err -> return raise (err.ToException())
        }

    interface IMailbox<'m>

