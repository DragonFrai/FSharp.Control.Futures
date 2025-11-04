namespace FSharp.Control.Futures.Actors

open System
open FSharp.Control.Futures
open FSharp.Control.Futures.Actors.Addressing
open FSharp.Control.Futures.Mail


[<Struct>]
type internal ActorProcessMessage =
    | Envelope of envelope: IEnvelope
    | SignalStop

type ActorProcess =
    val private _mailbox: Mailbox<IEnvelope>
    val private _address: MailboxAddress

    val mutable private _actor: IActor option
    // Not status.
    val mutable private _isStarted: bool
    val mutable private _status: ActorStatus
    val mutable private _exception: exn option

    val mutable _stopRequest: ActorStoppingKind option
    val mutable _stopCancel: bool

    private new (actor: IActor option) =
        // let mailbox: Mailbox<ActorProcessMessage> = Mailbox()
        let mailbox: Mailbox<IEnvelope> = Mailbox()
        // let envelopeMailbox = TransformMailbox(mailbox, ActorProcessMessage.Envelope, id)
        // let address: MailboxAddress = MailboxAddress(envelopeMailbox)
        let address: MailboxAddress = MailboxAddress(mailbox)
        {
            _actor = actor
            _mailbox = mailbox
            _address = address
            _isStarted = false
            _status = ActorStatus.Created
            _exception = None
            _stopRequest = None
            _stopCancel = false
        }

    new () = ActorProcess(None)
    new (actor: IActor) = ActorProcess(Some actor)

    member this.Handle: IActorHandle = this
    member this.Address: IDynamicAddress = this.Handle.Address

    member this.SetActor(actor: IActor): unit =
        if not (obj.ReferenceEquals(this._actor, null)) then
            failwith "Actor already set"
        this._actor <- Some actor

    member this.Start(): Future<unit> = future {
        // <utils>

        let inline catchExceptionStop (action: unit -> Future<unit>) : Future<unit> = future {
            let! res = Future.catch (action ())
            match res with
            | Ok () ->
                return ()
            | Error ex ->
                if this._stopRequest <> None then
                    this._stopRequest <- Some (ActorStoppingKind.Exception (ex, true))
                return ()
        }

        let inline doWithStoppingContext (action: unit -> Future<unit>) : Future<ActorStoppingKind voption> = future {
            do! catchExceptionStop action
            match this._stopRequest with
            | None -> return ValueNone
            | Some x -> return ValueSome x
        }



        // </utils>

        if this._isStarted then
            invalidOp "ActorMailbox can't be started twice"
        let actor =
            let actor = this._actor
            match actor with
            | None -> invalidOp "ActorMailbox without configured actor can't be start"
            | Some actor -> actor

        // match this._stopRequest |> Option.map _.Type with
        // | Some ActorStoppingKind.Request true -> return ()
        // | Some _ -> failwith "Impossible stop request before the actor started"
        // | None ->
        let mailbox = this._mailbox

        this._isStarted <- true

        this._status <- ActorStatus.Starting

        let! startResult = Future.catch (actor.Start(this))

        // match this._stopRequest |> Option.map _.Type with
        // | Some ActorStoppingType. ->
        //     req.
        //
        // | None -> ()




        let mutable loopResult: Result<unit, exn> option = None
        try
            while loopResult.IsNone do
                let! envelope = Mailbox.receive mailbox
                let! () = actor.Receive(failwith "TODO", envelope)
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

    interface IActorHandle with
        member this.Address = this._address
        member this.Status = this._status
        member this.Exception = this._exception

    interface IActorContext with

        member this.Address =
            this._address

        member this.Stop() =
            raise (NotImplementedException())
            this._stopRequest <- Some (ActorStoppingKind.Request false)

        member this.StopByException(ex) =
            raise (NotImplementedException())
            this._stopRequest <- Some (ActorStoppingKind.Exception (ex, false))

    interface IActorStopping with

        member this.Cancel() =
            raise (NotImplementedException())
            this._stopCancel <- true

        member this.Kind: ActorStoppingKind =
            raise (NotImplementedException())
            match this._stopRequest with
            | None -> invalidOp "ActorStoppingKind requested in not stopping state"
            | Some x -> x
