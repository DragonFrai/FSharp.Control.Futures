namespace rec FSharp.Control.Futures.Actors

open FSharp.Control.Futures
open FSharp.Control.Futures.Actors.Addressing


/// <summary>
/// Actor handler interface for handle one msg type.
/// </summary>
type IHandler<'i, 'o> =
    abstract Handle: ctx: IActorContext * msg: 'i * accept: IEnvelopeAccept<'o> -> Future<unit>

type internal HandlerActorVisitor() =
    static member Instance = HandlerActorVisitor()
    interface IEnvelopeVisitorFunc<struct (HandlerActor * IActorContext), Future<unit>> with
        member this.Visit<'i, 'o>(msg: 'i, accept: IEnvelopeAccept<'o>, args: struct (HandlerActor * IActorContext)) : Future<unit> = future {
            let struct (actor, ctx) = args
            match box actor with
            | :? IHandler<'i, 'o> as handler ->
                return! handler.Handle(ctx, msg, accept)
            | _ ->
                let actorTypeName = actor.GetType().Name
                let msgTypeName = typeof<Envelope<'i, 'o>>.Name
                let msgInTypeName = typeof<'i>.Name
                let msgOutTypeName = typeof<'o>.Name
                let exnMsg = $"Actor {actorTypeName} can not receive {msgTypeName}<{msgInTypeName}, {msgOutTypeName}>"
                do accept.ReplyExn(UnhandleableMessage(exnMsg))
                return ()
        }


[<AbstractClass>]
type HandlerActor =
    inherit BaseActor
    new() = {  }

    override this.Receive(ctx, dynMsg) = future {
        let visitor = HandlerActorVisitor.Instance
        return! dynMsg.AcceptFunc(visitor, struct (this, ctx))
    }
