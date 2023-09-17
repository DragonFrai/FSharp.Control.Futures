namespace FSharp.Control.Futures.Sync

open FSharp.Control.Futures
open FSharp.Control.Futures.Internals


/// Single Produces Single Consumer (SPSC)
type Reply<'a> =
    val mutable pIVar: PrimaryIVar<'a>
    new () = { pIVar = PrimaryIVar<'a>.Empty() }

    member this.Reply(msg: 'a) : unit =
        this.pIVar.Put(msg)

    interface Future<'a> with
        member this.Poll(ctx: IContext) : Poll<'a> =
            this.pIVar.PollGet(ctx) |> NaivePoll.toPoll

        member this.Drop() : unit =
            this.pIVar.Drop()
