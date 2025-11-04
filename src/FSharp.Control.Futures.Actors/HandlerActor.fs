namespace rec FSharp.Control.Futures.Actors

open FSharp.Control.Futures
open FSharp.Control.Futures.Actors.Addressing


/// <summary>
/// Actor handler interface for handle one msg type.
/// </summary>
type IHandler<'i, 'o> =
    abstract Handle: context: IActorContext * message: 'i * accept: IEnvelopeAccept<'o> -> Future<unit>

[<Struct>]
type internal VisitorArgs =
    { Actor: HandlerActor
      Context: IActorContext }

type internal HandlerActorVisitor() =
    static member Instance = HandlerActorVisitor()
    interface IEnvelopeVisitorFunc<VisitorArgs, Future<unit>> with
        member this.Visit<'i, 'o>(msg: 'i, accept: IEnvelopeAccept<'o>, args: VisitorArgs) : Future<unit> = future {
            let actor = args.Actor
            let context = args.Context
            match box actor with
            | :? IHandler<'i, 'o> as handler ->
                return! handler.Handle(context, msg, accept)
            | _ ->
                let actorTypeName = actor.GetType().Name
                let msgTypeName = typeof<Envelope<'i, 'o>>.Name
                let msgInTypeName = typeof<'i>.Name
                let msgOutTypeName = typeof<'o>.Name
                let exnMsg = $"Actor {actorTypeName} can not receive {msgTypeName}<{msgInTypeName}, {msgOutTypeName}>"
                do accept.ReplyExn(UnhandleableEnvelope(exnMsg))
                return ()
        }

[<AbstractClass>]
type HandlerActor =
    new() = {  }

    abstract Start: IActorContext -> Future<unit>
    abstract Stop: IActorContext * IActorStopping -> Future<unit>

    interface IActor with

        member this.Receive(context, dynMsg) = future {
            let visitor = HandlerActorVisitor.Instance
            let args: VisitorArgs = { Actor = this; Context = context }
            return! dynMsg.AcceptFunc(visitor, args)
        }

        member this.Start(context) = this.Start(context)
        member this.Stop(context, stopping) = this.Stop(context, stopping)
