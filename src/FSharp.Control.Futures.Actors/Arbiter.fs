namespace rec FSharp.Control.Futures.Actors

open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Sync




type ArbiterMsg =
    | Msg of msg: DynMsg


type ArbiterAddress(arbiterMailbox: Mailbox<ArbiterMsg>) =
    inherit BaseActorAddress()

    override this.Post(msg: DynMsg): Future<unit> = future {
        let arbMsg = ArbiterMsg.Msg msg
        do arbiterMailbox.Post(arbMsg)
        return ()
    }

type ArbiterContext(runtime: IRuntime, arbiter: Arbiter, arbiterAddress: ArbiterAddress) =
    interface IActorContext with
        member this.Spawn(fut: Future<'a>): IFutureTask<'a> =
            runtime.Spawn(fut)

        member this.SelfAddress: IActorAddress =
            arbiterAddress

        member this.Status: ActorStatus =
            failwith "TODO"

        member this.Stop(): unit =
            failwith "TODO"

        member this.Terminate(): unit =
            failwith "TODO"


// type ArbiterConcurrentState() =
//     let mutable status: ActorStatus = ActorStatus.Starting
//
//     val state: int
//     new(initial: state)


type Arbiter =
    val actorFactory: unit -> IActor
    val mutable isStarted: bool

    new(actorFactory) =
        { actorFactory = actorFactory
          isStarted = false }

    member this.Start(runtime: IRuntime): IActorAddress =
        if this.isStarted then invalidOp "Double start arbiter"
        this.isStarted <- true

        let mailbox = Mailbox()
        let actor = this.actorFactory ()
        let addr = ArbiterAddress(mailbox)

        let arbCtx = ArbiterContext(runtime, this, addr)

        let arbiterLoopFuture = future {
            do actor.Start(arbCtx)
            while true do
                let! msg = mailbox.Receive()
                match msg with
                | Msg msg ->
                    do! actor.Receive(arbCtx, msg)
            do actor.Stop(arbCtx)
            ()
        }

        // TODO: not ignore
        let arbiterTask = runtime.Spawn(arbiterLoopFuture)
        let () = arbiterTask |> ignore

        addr
