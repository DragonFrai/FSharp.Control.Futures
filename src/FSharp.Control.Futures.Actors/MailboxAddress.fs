namespace FSharp.Control.Futures.Actors

open FSharp.Control.Futures
open FSharp.Control.Futures.Actors.Addressing
open FSharp.Control.Futures.Mail


[<Class>]
type MailboxAddress =

    val private _mailbox: IOutbox<IEnvelope>

    new (outbox: IOutbox<IEnvelope>) =
        {
            _mailbox = outbox
        }

    interface IDynamicAddress with
        member this.Send<'m, 'r>(message: 'm): Future<'r> = future {
            let envelope: Envelope<'m, 'r> = Envelope.create message
            do! Mailbox.send (Envelope.box envelope) this._mailbox
            let! res = envelope.Awaiter
            match res with
            | Ok value -> return value
            | Error ex -> return raise ex
        }
