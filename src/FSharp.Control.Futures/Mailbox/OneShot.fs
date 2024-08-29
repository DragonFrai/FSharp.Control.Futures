namespace FSharp.Control.Futures.Mailbox

open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


/// <summary>
/// Single Produces Single Consumer (SPSC) for only one msg
/// </summary>
type OneShot<'a> =
    val mutable internal pIVar: PrimaryOnceCell<'a>
    new () = { pIVar = PrimaryOnceCell<'a>.Empty() }

    member this.Reply(msg: 'a) : unit =
        this.pIVar.Put(msg)

    member inline this.AsFuture() : Future<'a> = this

    interface Future<'a> with
        member this.Poll(ctx: IContext) : Poll<'a> =
            this.pIVar.Poll(ctx) |> NaivePoll.toPoll

        member this.Drop() : unit =
            this.pIVar.Drop()
