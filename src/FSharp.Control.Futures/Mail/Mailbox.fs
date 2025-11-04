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


type [<Sealed>] TransformMailbox<'from, 'into, 'inner> =

    val private _mailbox: IMailbox<'inner>
    val private _mapFrom: 'from -> 'inner
    val private _mapInto: 'inner -> 'into

    new(mailbox: IMailbox<'inner>, mapFrom: 'from -> 'inner, mapInto: 'inner -> 'into) =
        {
            _mailbox = mailbox
            _mapFrom = mapFrom
            _mapInto = mapInto
        }

    interface IMailbox with
        member this.Count = this._mailbox.Count
        member this.Bound = this._mailbox.Bound
        member this.IsCompleted = this._mailbox.IsCompleted

    interface IOutbox<'from> with
        member this.TrySend(msg) = future {
            return! this._mailbox.TrySend(this._mapFrom msg)
        }

        member this.Send(msg) = future {
            return! this._mailbox.Send(this._mapFrom msg)
        }

        member this.Push(msg) =
            this._mailbox.Push(this._mapFrom msg)

        member this.Complete() =
            this._mailbox.Complete()

    interface IInbox<'into> with

        member this.Pick() =
            this._mailbox.Pick() |> Result.map this._mapInto

        member this.Receive() =
            this._mailbox.Receive() |> Future.map this._mapInto

        member this.TryReceive() =
            this._mailbox.TryReceive() |> Future.map (Result.map this._mapInto)
