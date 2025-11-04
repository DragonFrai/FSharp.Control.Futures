namespace rec FSharp.Control.Futures.Actors

open System
open FSharp.Control.Futures
open FSharp.Control.Futures.Sync


[<Interface>]
type IEnvelopeAccept<'r> =
    abstract IsReplyed: bool
    abstract Reply: 'r -> unit
    abstract ReplyExn: exn -> unit

[<Interface>]
type IEnvelopeVisitor =
    abstract Visit<'m, 'r> : msg: 'm * accept: IEnvelopeAccept<'r> -> unit

[<Interface>]
type IEnvelopeVisitorFunc<'a, 'x> =
    abstract Visit<'m, 'r> : msg: 'm * accept: IEnvelopeAccept<'r> * args: 'a -> 'x

[<Interface>]
type IEnvelope =
    // TODO?: Remove
    abstract MsgType : Type
    // TODO?: Remove
    abstract ReplyType : Type
    abstract Accept : visitor: IEnvelopeVisitor -> unit
    abstract AcceptFunc<'a, 'x> : visitorFunc: IEnvelopeVisitorFunc<'a, 'x> * args: 'a -> 'x


[<Class>]
[<Sealed>]
type Envelope<'m, 'r> =
    val private _message: 'm
    val private _replyCh: OneShot<Result<'r, exn>>

    new(msg, reply) =
        { _message = msg; _replyCh = reply }

    member this.Message: 'm = this._message
    member this.Receive: OneSend<Result<'r, exn>> = this._replyCh.AsSend
    member this.Awaiter: Future<Result<'r, exn>> = this._replyCh

    interface IEnvelopeAccept<'r> with

        member this.IsReplyed: bool =
            this._replyCh.IsSent

        member this.Reply(reply: 'r) : unit =
            this._replyCh.DoSend(Ok reply)

        member this.ReplyExn(ex: exn) : unit =
            this._replyCh.DoSend(Error ex)

    interface IEnvelope with

        member this.MsgType: Type =
            typeof<'m>

        member this.ReplyType: Type =
            typeof<'r>

        member this.Accept(visitor: IEnvelopeVisitor): unit =
            visitor.Visit<'m, 'r>(this._message, this :> IEnvelopeAccept<'r>)

        member this.AcceptFunc<'a, 'x>(visitorFunc: IEnvelopeVisitorFunc<'a, 'x>, arg: 'a): 'x =
            visitorFunc.Visit<'m, 'r>(this._message, this :> IEnvelopeAccept<'r>, arg)


[<RequireQualifiedAccess>]
module Envelope =

    let inline create<'m ,'r> (msg: 'm) : Envelope<'m, 'r> =
        Envelope<'m, 'r>(msg, OneShot())

    let inline box (msg: Envelope<'i, 'o>) : IEnvelope =
        upcast msg
