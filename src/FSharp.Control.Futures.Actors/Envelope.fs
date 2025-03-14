namespace rec FSharp.Control.Futures.Actors

open System
open System.Threading
open FSharp.Control.Futures.Sync


[<Interface>]
type IEnvelopeVisitor =
    abstract Visit<'i, 'o> : msg: Envelope<'i, 'o> -> unit

[<Interface>]
type IEnvelopeVisitorFunc<'a, 'r> =
    abstract Visit<'i, 'o> : msg: Envelope<'i, 'o> * arg: 'a -> 'r

// [<RequireQualifiedAccess>]
// [<Struct>]
// type internal EnvelopeReplyValue<'r> =
//     | Empty of isRequested: bool
//     | Full of 'r
//
// [<Struct>]
// type EnvelopeReply<'r> =
//     val mutable private value: EnvelopeReplyValue<'r>
//     new (isRequested: bool) = { value = EnvelopeReplyValue.Empty isRequested }
//
//     member this.IsRequested: bool =
//         match this.value with
//         | EnvelopeReplyValue.Empty isRequested -> isRequested
//         | EnvelopeReplyValue.Full _ -> true // TODO?: Or invalidOp
//
//     member this.Reply(value: 'r): unit =
//         match this.value with
//         | EnvelopeReplyValue.Empty true ->
//             this.value <- EnvelopeReplyValue.Full value
//         | EnvelopeReplyValue.Empty false ->
//             ()
//         | EnvelopeReplyValue.Full _ ->
//             invalidOp "Multiple reply"

[<AbstractClass>]
type IEnvelope internal () =
    abstract MsgType: Type
    abstract ReplyType: Type
    abstract Accept : IEnvelopeVisitor -> unit
    abstract AcceptFunc<'a, 'r> : visitorFunc: IEnvelopeVisitorFunc<'a, 'r> * arg: 'a -> 'r
    abstract Cast<'i, 'o> : unit -> Envelope<'i, 'o>




[<Class>]
[<Sealed>]
type Envelope<'i, 'o> =
    val Message: 'i
    val ReplyValue: OneShotSender<'o>

    new(msg, reply) =
        { inherit IEnvelope(); Message = msg; ReplyValue = reply }

    new(msg) =
        let os = OneShot.Closed
        { inherit IEnvelope(); Message = msg; ReplyValue = os.Sender }

    member this.IsReplyOpened: bool =
        not this.ReplyValue.IsClosed

    member this.Reply(reply: 'o): unit =
        do this.ReplyValue.Send(reply) |> ignore
        ()

    member this.ReplyWith(reply: unit -> 'o): unit =
        if this.IsReplyOpened then
            do this.Reply(reply ())

    inherit IEnvelope with

        override this.MsgType: Type =
            typeof<'i>

        override this.ReplyType: Type =
            typeof<'o>

        override this.Accept(visitor: IEnvelopeVisitor): unit =
            visitor.Visit<'i, 'o>(this)

        override this.AcceptFunc<'a, 'r>(visitorFunc: IEnvelopeVisitorFunc<'a, 'r>, arg: 'a): 'r =
            visitorFunc.Visit<'i, 'o>(this, arg)

        override this.Cast<'m, 'r>(): Envelope<'m, 'r> =
            unbox this

[<RequireQualifiedAccess>]
module Envelope =
    let create (msg: 'i) (reply: OneShotSender<'o>) : Envelope<'i, 'o> =
        Envelope<'i, 'o>(msg, reply)

    let createDyn (msg: 'i) (reply: OneShotSender<'o>) : IEnvelope =
        Envelope<'i, 'o>(msg, reply)

    let box (msg: Envelope<'i, 'o>) : IEnvelope =
        msg

    let unbox<'i, 'o> (msgBox: IEnvelope) : Envelope<'i, 'o> =
        msgBox.Cast<'i, 'o>()
