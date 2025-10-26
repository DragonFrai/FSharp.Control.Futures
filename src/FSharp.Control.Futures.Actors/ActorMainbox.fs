namespace FSharp.Control.Futures.Actors

open FSharp.Control.Futures
open FSharp.Control.Futures.Actors.Addressing
open FSharp.Control.Futures.Mail


[<Class>]
type ActorMailbox =

    val private _mailbox: Mailbox<IEnvelope>
    val mutable private _actor: IActor
    val mutable private _isStarted: bool

    new (actor: IActor) =
        {
            _mailbox = Mailbox()
            _actor = actor
            _isStarted = false
        }

    new () =
        {
            _mailbox = Mailbox()
            _actor = Unchecked.defaultof<IActor>
            _isStarted = false
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

    member this.SetActor(actor: IActor): unit =
        if not (obj.ReferenceEquals(this._actor, null)) then
            failwith "Actor already set"
        this._actor <- actor

    member this.Start(): Future<unit (* Never? *)> = future {
        if this._isStarted then
            invalidOp "ActorMailbox can't be started twice"
        let actor =
            let actor = this._actor
            if obj.ReferenceEquals(actor, null) then
                invalidOp "ActorMailbox without configured actor can't be start"
            actor
        let mailbox = this._mailbox

        this._isStarted <- true
        let mutable loopResult: Result<unit, exn> option = None
        try
            while loopResult.IsNone do
                let! envelope = Mailbox.receive mailbox
                let! () = actor.Receive(envelope)
                ()
            ()
        with ex ->
            loopResult <- Some (Error ex)
        this._isStarted <- false

        let result = loopResult |> Option.get
        match result with
        | Ok () -> return ()
        | Error ex -> return raise ex
    }

