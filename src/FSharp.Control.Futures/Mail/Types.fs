namespace FSharp.Control.Futures.Mail

open System
open FSharp.Control.Futures
open FSharp.Control.Futures.Sync


type MailboxException =
    inherit Exception
    new () = { inherit Exception() }
    new (message: string) = { inherit Exception(message) }

type MailboxCompletedException =
    inherit MailboxException
    new () = { inherit MailboxException("Channel is completed") }
    new (message: string) = { inherit MailboxException(message) }

type MailboxFullException =
    inherit MailboxException
    new () = { inherit MailboxException("Channel is full") }
    new (message: string) = { inherit MailboxException(message) }

type MailboxEmptyException =
    inherit MailboxException
    new () = { inherit MailboxException("Channel is empty") }
    new (message: string) = { inherit MailboxException(message) }


[<Struct>]
[<RequireQualifiedAccess>]
type MailboxError =
    | Completed
    with
        member this.ToException(): Exception =
            match this with
            | MailboxError.Completed -> MailboxCompletedException()

[<Struct>]
[<RequireQualifiedAccess>]
type MailboxPushError =
    | Completed
    | Full
    with
        member this.ToException(): Exception =
            match this with
            | MailboxPushError.Completed -> MailboxCompletedException()
            | MailboxPushError.Full -> MailboxFullException()

[<Struct>]
[<RequireQualifiedAccess>]
type MailboxPickError =
    | Completed
    | Empty
    with
        member this.ToException(): Exception =
            match this with
            | MailboxPickError.Completed -> MailboxCompletedException()
            | MailboxPickError.Empty -> MailboxEmptyException()

// TODO: Add unbounded channels

[<Interface>]
type IMailbox =
    abstract IsCompleted: bool
    abstract Count: int
    /// Channel elements bound or 0 if no bounds
    abstract Bound: int

[<Interface>]
type IOutbox<'m> =
    inherit IMailbox
    abstract TrySend: msg: 'm -> Future<Result<unit, MailboxError>>
    abstract Send: msg: 'm -> Future<unit>
    abstract Push: msg: 'm -> Result<unit, MailboxPushError>
    abstract Complete: unit -> unit

[<Interface>]
type IInbox<'m> =
    inherit IMailbox
    abstract TryReceive: unit -> IFuture<Result<'m, MailboxError>>
    abstract Receive: unit -> IFuture<'m>
    abstract Pick: unit -> Result<'m, MailboxPickError>

[<Interface>]
type IMailbox<'m> =
    inherit IMailbox
    inherit IOutbox<'m>
    inherit IInbox<'m>


[<AutoOpen>]
module OutboxExtensions =
    type IOutbox<'m> with
        member this.TrySendWithReply<'r>(msgBuilder: OneSend<'r> -> 'm): Future<Result<'r, MailboxError>> = future {
            let os = OneShot.create ()
            let msg = msgBuilder os.AsSend
            let! res = this.TrySend(msg)
            match res with
            | Ok () ->
                let! value = os
                return Ok value
            | Error err ->
                return Error err
        }

        member this.SendWithReply<'r>(msgBuilder: OneSend<'r> -> 'm): Future<'r> = future {
            let os = OneShot.create ()
            let msg = msgBuilder os.AsSend
            do! this.Send(msg)
            return! os
        }

[<RequireQualifiedAccess>]
module Mailbox =
    let inline isCompleted (mailbox: IMailbox) : bool =
        mailbox.IsCompleted

    let inline count (mailbox: IMailbox) : int =
        mailbox.Count

    let inline bound (mailbox: IMailbox) : int =
        mailbox.Bound

    let inline send (msg: 'm) (mailbox: IOutbox<'m>) : Future<unit> =
        mailbox.Send(msg)

    let inline trySend (msg: 'm) (mailbox: IOutbox<'m>) : Future<Result<unit, MailboxError>> =
        mailbox.TrySend(msg)

    let inline complete (mailbox: IOutbox<'m>) : unit =
        mailbox.Complete()

    let inline push (msg: 'm) (mailbox: IOutbox<'m>) : Result<unit, MailboxPushError> =
        mailbox.Push(msg)

    let inline receive (mailbox: IInbox<'m>) : Future<'m> =
        mailbox.Receive()

    let inline tryReceive (mailbox: IInbox<'m>) : Future<Result<'m, MailboxError>> =
        mailbox.TryReceive()

    let inline pick (mailbox: IInbox<'m>) : Result<'m, MailboxPickError> =
        mailbox.Pick()
