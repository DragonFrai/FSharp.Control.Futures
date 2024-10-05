namespace FSharp.Control.Futures.Actors

open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Sync


type ActorStatus =
    | Starting
    | Started
    | Stopping
    | Stopped

    with
        member this.IsAlive: bool =
            match this with
            | Starting | Started -> true
            | Stopping | Stopped -> false

        member this.IsDead: bool =
            match this with
            | Starting | Started -> false
            | Stopping | Stopped -> true


type IArbiter =
    abstract Status: ActorStatus
    abstract Address: IActorAddress
    abstract Stop: unit -> Future<unit>


type ArbiterMsg =
    | Msg of msg: DynMsg


type ArbiterAddress(arbiterMailbox: Mailbox<ArbiterMsg>) =
    inherit BaseActorAddress()

    override this.Post(msg: DynMsg): Future<unit> = future {
        // Exn if stopped
        let arbMsg = ArbiterMsg.Msg msg
        do arbiterMailbox.Post(arbMsg)
        return ()
    }

type ArbiterContext(mailbox: Mailbox<ArbiterMsg>, selfAddress: IActorAddress, backgroundRuntime: IRuntime) =

    interface IActorContext with

        member this.Spawn(fut: Future<'a>): IFutureTask<'a> =
            backgroundRuntime.Spawn(fut)

        member this.SelfAddress: IActorAddress =
            selfAddress

        member this.Stop(): unit =
            failwith "TODO"

        member this.Terminate(): unit =
            failwith "TODO"

[<RequireQualifiedAccess>]
module ActorId =
    let mutable private salt: uint = 0u
    let internal nextSalt () =
        Interlocked.Add(&salt, 1u)

[<Class>]
type ActorId =
    val private _id: string

    new(name: string, addSalt: bool) =
        if name = "" then invalidArg "name" "name can't be empty"
        let id =
            if addSalt then
                let salt = ActorId.nextSalt ()
                $"{name}-{salt}"
            else
                name
        { _id = id }
    new(name: string) = ActorId(name, true)
    new() = ActorId("Actor", true)

    override this.ToString() = this._id

type ArbiterDescriptor =
    { Actor: IActor
      ActorId: ActorId
      MessageLoopRuntime: IRuntime
      BackgroundRuntime: IRuntime
      //QueueCapacity: int
      }


type Arbiter =

    val internal actor: IActor
    val internal actorId: ActorId
    val internal msgLoopRuntime: IRuntime
    val internal backgroundRuntime: IRuntime

    val internal mailbox: Mailbox<ArbiterMsg>
    val internal address: IActorAddress
    val internal context: IActorContext

    member this.Address: IActorAddress = this.address

    private new (actor, actorId, msgLoopRuntime, backgroundRuntime, mailbox, address, context) =
        { actor = actor
          actorId = actorId
          msgLoopRuntime = msgLoopRuntime
          backgroundRuntime = backgroundRuntime
          mailbox = mailbox
          address = address
          context = context }

    static member Start(desc: ArbiterDescriptor): Arbiter =
        let actor = desc.Actor
        let mailbox = Mailbox()
        let addr = ArbiterAddress(mailbox)
        let arbCtx = ArbiterContext(mailbox, addr, desc.BackgroundRuntime)

        let worker = future {
            do actor.Start(arbCtx)
            while true do
                let! msg = mailbox.Receive()
                match msg with
                | Msg msg ->
                    do! actor.Receive(arbCtx, msg)
            do actor.Stop(arbCtx)
        }

        // TODO: not ignore
        let arbiterTask = desc.MessageLoopRuntime.Spawn(worker)
        let () = arbiterTask |> ignore

        Arbiter(actor, desc.ActorId, desc.MessageLoopRuntime, desc.BackgroundRuntime, mailbox, addr, arbCtx)
