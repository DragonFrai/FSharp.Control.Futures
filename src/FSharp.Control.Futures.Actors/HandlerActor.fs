namespace rec FSharp.Control.Futures.Actors

open FSharp.Control.Futures


/// <summary>
/// Actor handler interface for handle one msg type.
/// </summary>
type IHandler<'i, 'o> =
    abstract Handle: ctx: IActorContext * msg: Msg<'i, 'o> -> Future<unit>

type internal HandlerActorVisitor() =
    static member Instance = HandlerActorVisitor()
    interface IMsgVisitorFunc<struct (HandlerActor * IActorContext), Future<unit>> with
        member this.Visit<'i, 'o>(msg: Msg<'i, 'o>, arg: struct (HandlerActor * IActorContext)) : Future<unit> =
            let struct (actor, ctx) = arg
            match box actor with
            | :? IHandler<'i, 'o> as handler ->
                actor.OnReceive(ctx, msg)
                handler.Handle(ctx, msg)
            | _ ->
                let actorTypeName = actor.GetType().Name
                let msgTypeName = nameof Msg<'i, 'o>
                let msgInTypeName = typeof<'i>.Name
                let msgOutTypeName = typeof<'o>.Name
                failwith $"Actor {actorTypeName} can not receive {msgTypeName}<{msgInTypeName}, {msgOutTypeName}>"

[<AbstractClass>]
type HandlerActor =
    inherit BaseActor
    new() = {  }

    abstract OnReceive: ctx: IActorContext * dynMSg: DynMsg -> unit
    default this.OnReceive(_ctx: IActorContext, _dynMSg: DynMsg): unit =
        ()

    // TODO:
    // [<Sealed>]
    override this.Receive(ctx, dynMsg) = future {
        let visitor = HandlerActorVisitor.Instance
        return! dynMsg.Accept(visitor, struct (this, ctx))
    }
